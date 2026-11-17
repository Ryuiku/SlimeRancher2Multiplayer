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
    [HarmonyPostfix, HarmonyPatch(nameof(EndGameRewardsDataModel.SetItemProgress))]
    public static void SetItem(string rewardId, IdentifiableType identifiableType, int countCollected)
    {
        if (!identifiableType)
            return;

        Send(EndGameRewardProgressPacket.ProgressOp.Set, rewardId, identifiableType, countCollected);
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

    private static void Send(EndGameRewardProgressPacket.ProgressOp op, string rewardId, IdentifiableType? type, int count)
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
                Op = op
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
[HarmonyPatch(typeof(RewardsKiosk), nameof(RewardsKiosk.DispenseRewards))]
internal static class OnKioskDispense
{
    // How long after a progress packet a payout that this game did not deposit for is held back,
    // in case the kiosk pays out a little later than the packet (from an animation).
    private const float RemoteWindowSeconds = 10f;

    internal static float LastRemoteProgress = float.NegativeInfinity;

    public static bool Prefix(string rewardTrackId)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
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

        return true;
    }
}
