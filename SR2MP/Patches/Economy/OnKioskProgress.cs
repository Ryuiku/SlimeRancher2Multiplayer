using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Prestige;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.Economy;

// The rewards kiosk keeps its progress per player in the game. Shared here, so everyone's
// deposits count towards the same tracks.
[HarmonyPatch(typeof(EndGameRewardsDataModel))]
internal static class OnKioskProgress
{
    // The count before the change goes with it, so the host can tell two deposits made at the
    // same time apart from one (see UnlockLookup.MergeConcurrentRewardProgress).
    [HarmonyPrefix, HarmonyPatch(nameof(EndGameRewardsDataModel.SetItemProgress))]
    public static void SetItemPrefix(EndGameRewardsDataModel __instance, string rewardId,
        IdentifiableType identifiableType, out int __state)
    {
        __state = -1;
        try
        {
            if (identifiableType && !string.IsNullOrEmpty(rewardId))
                __state = __instance.GetItemProgressOrDefault(rewardId, identifiableType);
        }
        catch
        {
            __state = -1;
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(EndGameRewardsDataModel.SetItemProgress))]
    public static void SetItem(string rewardId, IdentifiableType identifiableType, int countCollected, int __state)
    {
        if (!identifiableType)
            return;

        Send(EndGameRewardProgressPacket.ProgressOp.Set, rewardId, identifiableType, countCollected, __state);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(EndGameRewardsDataModel.ClearItemProgress))]
    public static void ClearItem(string rewardId, IdentifiableType identifiableType)
    {
        if (!identifiableType)
            return;

        Send(EndGameRewardProgressPacket.ProgressOp.ClearItem, rewardId, identifiableType, 0);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(EndGameRewardsDataModel.ClearRewardProgress))]
    public static void ClearReward(string rewardId, bool __result)
    {
        if (!__result)
            return;

        Send(EndGameRewardProgressPacket.ProgressOp.ClearReward, rewardId, null, 0);
    }

    private static void Send(EndGameRewardProgressPacket.ProgressOp op, string rewardId, IdentifiableType? type, int count,
        int previous = -1)
    {
        if (string.IsNullOrEmpty(rewardId) || !UnlockLookup.CanSend)
            return;

        try
        {
            Main.SendToAllOrServer(new EndGameRewardProgressPacket
            {
                RewardId = rewardId,
                IdentId = type ? NetworkActorManager.GetPersistentID(type!) : -1,
                Count = count,
                Op = op,
                Previous = previous
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share rewards kiosk progress: {ex.Message}");
        }
    }
}

// Every player's game sees the same item fly into the kiosk. Only the player simulating it
// deposits it, otherwise one item would count once per player now that progress is shared.
[HarmonyPatch(typeof(ResourceDepositor), nameof(ResourceDepositor.OnTriggerEnter))]
internal static class OnKioskDeposit
{
    // When this game last let an item into the kiosk; see OnKioskDispense.
    internal static float LastLocalDeposit = float.NegativeInfinity;

    public static bool Prefix(Collider otherCollider)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                return true;

            if (otherCollider)
            {
                var networkActor = otherCollider.GetComponentInParent<SR2MP.Components.Actor.NetworkActor>();
                if (networkActor && !networkActor.LocallyOwned)
                    return false;
            }

            LastLocalDeposit = UnityEngine.Time.time;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"OnKioskDeposit prefix failed: {ex.Message}");
        }

        return true;
    }
}

// Rewards come out only at the kiosk of the player whose deposit completed the track. Progress
// written from the network must never pay out a second time on another machine.
//
// Two players can also complete the same track with deposits made at the same time: each game
// sees its own deposit finish it and pays out. So the host decides: it pays out its own, and a
// client asks it first and pays out only when the host says so. A track that just paid out
// anywhere does not pay out again for a few seconds.
[HarmonyPatch(typeof(RewardsKiosk), nameof(RewardsKiosk.DispenseRewards))]
internal static class OnKioskDispense
{
    // How long after a progress packet a payout that this game did not deposit for is held back,
    // in case the kiosk pays out a little later than the packet (from an animation).
    private const float RemoteWindowSeconds = 10f;

    // Host: a track that paid out this recently does not pay out again (one completion, two
    // deposits). Far shorter than it takes to deposit a whole track's cost again.
    private const float PayoutWindowSeconds = 3f;

    // Client: how long a payout waits for the host's answer before it is given up.
    private const float ClaimTimeoutSeconds = 10f;

    internal static float LastRemoteProgress = float.NegativeInfinity;

    // Host: when each track last paid out, here or at a client it allowed.
    private static readonly Dictionary<string, float> LastPayout = new();

    // Client: payouts waiting for the host's go-ahead, by track.
    private static readonly Dictionary<string, (Il2CppSystem.Collections.Generic.IReadOnlyList<EndGameRewardItem> Items, float Since)> Held = new();

    private static bool granting;

    static OnKioskDispense()
    {
        Main.Server.OnServerStarted +=      ClearSession;
        Main.Client.OnConnected     += _ => ClearSession();
        Main.Client.OnDisconnected  +=      ClearSession;
    }

    private static void ClearSession()
    {
        LastPayout.Clear();
        Held.Clear();
    }

    public static bool Prefix(string rewardTrackId, Il2CppSystem.Collections.Generic.IReadOnlyList<EndGameRewardItem> __1)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return true;

        // The payout the host allowed.
        if (granting)
            return true;

        if (HandlingPacket)
        {
            SrLogger.LogMessage($"Rewards kiosk: held back the {rewardTrackId} rewards, another player's deposit completed it");
            return false;
        }

        var now = UnityEngine.Time.time;
        if (now - LastRemoteProgress <= RemoteWindowSeconds && LastRemoteProgress > OnKioskDeposit.LastLocalDeposit)
        {
            SrLogger.LogMessage($"Rewards kiosk: held back the {rewardTrackId} rewards shortly after another player's progress");
            return false;
        }

        if (string.IsNullOrEmpty(rewardTrackId))
            return true;

        if (Main.Server.IsRunning)
        {
            if (PaidOutJustNow(rewardTrackId))
            {
                SrLogger.LogMessage($"Rewards kiosk: held back the {rewardTrackId} rewards, the track was just paid out to another player");
                return false;
            }

            LastPayout[rewardTrackId] = UnityEngine.Time.realtimeSinceStartup;
            return true;
        }

        Held[rewardTrackId] = (KeepItems(__1), UnityEngine.Time.realtimeSinceStartup);
        Main.SendToAllOrServer(new EndGameRewardProgressPacket
        {
            RewardId = rewardTrackId,
            Op = EndGameRewardProgressPacket.ProgressOp.ClaimPayout
        });

        SrLogger.LogMessage($"Rewards kiosk: the {rewardTrackId} rewards wait for the host's go-ahead");
        return false;
    }

    // The list the game passed in may be reused once the call returns; a copy is kept instead.
    private static Il2CppSystem.Collections.Generic.IReadOnlyList<EndGameRewardItem> KeepItems(
        Il2CppSystem.Collections.Generic.IReadOnlyList<EndGameRewardItem> items)
    {
        try
        {
            var copy = new Il2CppSystem.Collections.Generic.List<EndGameRewardItem>(
                items.Cast<Il2CppSystem.Collections.Generic.IEnumerable<EndGameRewardItem>>());
            return copy.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<EndGameRewardItem>>();
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Rewards kiosk: could not copy the reward list, keeping the game's: {ex.Message}");
            return items;
        }
    }

    private static bool PaidOutJustNow(string trackId)
        => LastPayout.TryGetValue(trackId, out var last)
           && UnityEngine.Time.realtimeSinceStartup - last < PayoutWindowSeconds;

    /// <summary>Host: a client's deposit completed a track.</summary>
    internal static void OnClaim(string trackId, System.Net.IPEndPoint? client)
    {
        if (string.IsNullOrEmpty(trackId) || client == null)
            return;

        if (PaidOutJustNow(trackId))
        {
            SrLogger.LogMessage($"Rewards kiosk: a player's {trackId} payout was turned down, the track was just paid out");
            return;
        }

        LastPayout[trackId] = UnityEngine.Time.realtimeSinceStartup;
        Main.Server.SendToClient(new EndGameRewardProgressPacket
        {
            RewardId = trackId,
            Op = EndGameRewardProgressPacket.ProgressOp.GrantPayout
        }, client);
    }

    /// <summary>Client: the host allowed the payout this game held.</summary>
    internal static void OnGrant(string trackId)
    {
        if (string.IsNullOrEmpty(trackId) || !Held.Remove(trackId, out var held))
            return;

        if (UnityEngine.Time.realtimeSinceStartup - held.Since > ClaimTimeoutSeconds)
        {
            SrLogger.LogMessage($"Rewards kiosk: the go-ahead for {trackId} came too late; not paid out");
            return;
        }

        var kiosk = RewardsKiosk.Instance;
        if (!kiosk)
        {
            SrLogger.LogWarning($"Rewards kiosk: the {trackId} rewards were allowed, but the kiosk is gone here");
            return;
        }

        granting = true;
        try
        {
            kiosk.DispenseRewards(trackId, held.Items);
            SrLogger.LogMessage($"Rewards kiosk: paid out {trackId} with the host's go-ahead");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Rewards kiosk: could not pay out {trackId}: {ex.Message}");
        }
        finally
        {
            granting = false;
        }
    }
}
