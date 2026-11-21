using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime;
using SR2MP.Components.Actor;
using SR2MP.Packets.Slime.Tabby;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Slime.Tabby;

// The tabby's owner tells the others each stalk step (approach, wait, crouch, pounce) and when it
// goes into or out of stealth. Without this the others saw the tabby slide and jump at its prey with
// no crouch, and a tabby could be invisible for one player and visible for another.
[HarmonyPatch(typeof(StalkConsumable))]
internal static class StalkConsumableSync
{
    private readonly record struct StalkState(byte Mode, long Target, bool Stealth);

    // What was last sent for each tabby this game owns. A tabby with no entry is idle and visible.
    private static readonly Dictionary<long, StalkState> LastSent = new();

    // The stealth the game last gave each tabby this game owns, from the SetStealth argument. The
    // slime's own IsStealthed is also true for a moment after it spawns, which must not be sent:
    // that would leave the tabby invisible on the other screens.
    private static readonly Dictionary<long, bool> OwnerStealth = new();

    // Tabbies this game was told to stealth by their previous owner. If this game becomes the
    // owner it has to end that stealth itself, or the tabby stays invisible.
    private static readonly Dictionary<long, bool> StealthFromPacket = new();

    private static bool setStealthPatchFired;
    private static bool loggedStealthFallback;
    private static bool loggedError;

    static StalkConsumableSync()
    {
        Main.Server.OnServerStarted +=      Clear;
        Main.Client.OnConnected     += _ => Clear();
        Main.Client.OnDisconnected  +=      Clear;
    }

    private static void Clear()
    {
        LastSent.Clear();
        OwnerStealth.Clear();
        StealthFromPacket.Clear();
    }

    /// <summary>
    /// Called by the SlimeStalk handler: another player now drives this tabby, so what this game
    /// sent before no longer says anything about what the others see.
    /// </summary>
    internal static void OnReceived(long actorId, bool stealth)
    {
        LastSent.Remove(actorId);
        OwnerStealth.Remove(actorId);
        StealthFromPacket[actorId] = stealth;
    }

    private static void LogOnce(string where, Exception exception)
    {
        if (loggedError)
            return;

        loggedError = true;
        SrLogger.LogDebug($"Tabby stalk sync ({where}) failed: {exception.Message}");
    }

    private static bool TryGetOwnedActorId(StalkConsumable stalk, out long actorId)
    {
        actorId = 0;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return false;

        if (HandlingPacket || !NetworkActorManager.CanSendActorChanges)
            return false;

        var networkActor = stalk.GetComponent<NetworkActor>();
        if (!networkActor || !networkActor.LocallyOwned)
            return false;

        actorId = networkActor.ActorId.Value;
        return actorId != 0;
    }

    private static long TargetActorId(StalkConsumable stalk)
    {
        var target = stalk._target;
        if (!target)
            return 0;

        var targetActor = target.GetComponentInParent<NetworkActor>();
        return targetActor ? targetActor.ActorId.Value : 0;
    }

    private static bool CurrentStealth(StalkConsumable stalk, long actorId)
    {
        if (setStealthPatchFired)
            return OwnerStealth.TryGetValue(actorId, out var stealth) && stealth;

        // The SetStealth patch has never run, so the game may have built it into its caller. Fall
        // back to what the slime shows, which is right except just after it spawns.
        var slimeStealth = stalk._stealth;
        var isStealthed = slimeStealth && slimeStealth.IsStealthed;

        if (isStealthed && !loggedStealthFallback)
        {
            loggedStealthFallback = true;
            SrLogger.LogMessage("Tabby stealth: the SetStealth patch is not firing, sending the slime's visible stealth instead");
        }

        return isStealthed;
    }

    private static void SendIfChanged(StalkConsumable stalk, long actorId, byte mode)
    {
        var state = new StalkState(mode, TargetActorId(stalk), CurrentStealth(stalk, actorId));

        var last = LastSent.TryGetValue(actorId, out var sent) ? sent : default;
        if (state == last)
            return;

        LastSent[actorId] = state;

        Main.SendToAllOrServer(new SlimeStalkPacket
        {
            ActorId = new Il2CppMonomiPark.SlimeRancher.DataModel.ActorId(actorId),
            Mode = state.Mode,
            TargetActorId = state.Target,
            Stealth = state.Stealth
        });
    }

    // Quick check for the physics-tick callers: only look further when the step or stealth changed.
    private static void CheckForChange(StalkConsumable stalk)
    {
        if (!TryGetOwnedActorId(stalk, out var actorId))
            return;

        var mode = (byte)stalk._mode;
        var last = LastSent.TryGetValue(actorId, out var sent) ? sent : default;
        if (last.Mode == mode && last.Stealth == CurrentStealth(stalk, actorId))
            return;

        SendIfChanged(stalk, actorId, mode);
    }

    // This game just became the owner of a tabby the previous owner had stealthed. If the game
    // does not stealth it again itself, end that stealth (the change is sent to the others too).
    private static void EndStealthFromPacket(StalkConsumable stalk)
    {
        if (!TryGetOwnedActorId(stalk, out var actorId))
            return;

        if (!StealthFromPacket.Remove(actorId, out var stealthed) || !stealthed)
            return;

        if (OwnerStealth.TryGetValue(actorId, out var ownStealth) && ownStealth)
            return;

        stalk.SetStealth(false);
    }

    [HarmonyPatch(nameof(StalkConsumable.SetMode)), HarmonyPostfix]
    public static void SetModePostfix(StalkConsumable __instance, [HarmonyArgument(0)] StalkConsumable.Mode mode)
    {
        try
        {
            if (TryGetOwnedActorId(__instance, out var actorId))
                SendIfChanged(__instance, actorId, (byte)mode);
        }
        catch (Exception exception)
        {
            LogOnce("SetMode", exception);
        }
    }

    [HarmonyPatch(nameof(StalkConsumable.SetStealth)), HarmonyPostfix]
    public static void SetStealthPostfix(StalkConsumable __instance, [HarmonyArgument(0)] bool isStealthed)
    {
        try
        {
            if (!TryGetOwnedActorId(__instance, out var actorId))
                return;

            setStealthPatchFired = true;
            OwnerStealth[actorId] = isStealthed;
            StealthFromPacket.Remove(actorId);

            SendIfChanged(__instance, actorId, (byte)__instance._mode);
        }
        catch (Exception exception)
        {
            LogOnce("SetStealth", exception);
        }
    }

    [HarmonyPatch(nameof(StalkConsumable.Selected)), HarmonyPostfix]
    public static void SelectedPostfix(StalkConsumable __instance, bool __runOriginal)
    {
        try
        {
            if (!__runOriginal)
                return;

            EndStealthFromPacket(__instance);
            CheckForChange(__instance);
        }
        catch (Exception exception)
        {
            LogOnce("Selected", exception);
        }
    }

    // SetMode and SetStealth are small; if the game built them into Action their patches never
    // run, so the step is also compared here.
    [HarmonyPatch(nameof(StalkConsumable.Action)), HarmonyPostfix]
    public static void ActionPostfix(StalkConsumable __instance, bool __runOriginal)
    {
        try
        {
            if (__runOriginal)
                CheckForChange(__instance);
        }
        catch (Exception exception)
        {
            LogOnce("Action", exception);
        }
    }

    [HarmonyPatch(nameof(StalkConsumable.Deselected)), HarmonyPostfix]
    public static void DeselectedPostfix(StalkConsumable __instance, bool __runOriginal)
    {
        try
        {
            if (!__runOriginal)
                return;

            EndStealthFromPacket(__instance);
            CheckForChange(__instance);
        }
        catch (Exception exception)
        {
            LogOnce("Deselected", exception);
        }
    }
}
