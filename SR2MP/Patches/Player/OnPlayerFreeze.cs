using HarmonyLib;
using Il2CppMonomiPark.KFC;
using Il2CppMonomiPark.SlimeRancher.Cutscene;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.Player;

// Everything here only writes down what happened (Shared/Utils/FreezeLog.cs) and never changes
// what the game does. A player once stood frozen with the UI still working, and the log had
// nothing on who froze them; these are the game's own ways to hold the player in place.

internal static class OnPlayerFreezeHelpers
{
    internal static bool IsLocalPlayer(Component component)
    {
        try
        {
            if (!component || !SceneContext.Instance || !SceneContext.Instance.player)
                return false;

            // The controller sits on the player object or right next to it in the hierarchy.
            var player = SceneContext.Instance.player.transform;
            var own = component.transform;
            return own.IsChildOf(player) || player.IsChildOf(own);
        }
        catch
        {
            return false;
        }
    }

    internal static string Source() => $"{(HandlingPacket ? "while applying a packet, " : string.Empty)}via {FreezeLog.ModCallers()}";

    internal static double HoursFromNow(double worldTime)
    {
        try
        {
            return (worldTime - SceneContext.Instance.TimeDirector._worldModel.worldTime) / 3600;
        }
        catch
        {
            return 0;
        }
    }
}

[HarmonyPatch(typeof(SRCharacterController), nameof(SRCharacterController.SetFreeze))]
internal static class OnPlayerControllerFreeze
{
    public static void Postfix(SRCharacterController __instance, [HarmonyArgument(0)] bool freeze)
    {
        try
        {
            if (!OnPlayerFreezeHelpers.IsLocalPlayer(__instance))
                return;

            // Repeated calls with the same value say nothing new.
            if (freeze == FreezeLog.ControllerFrozen)
                return;

            var source = OnPlayerFreezeHelpers.Source();
            FreezeLog.SetControllerFrozen(freeze, source);
            FreezeLog.Record($"Player controller {(freeze ? "frozen" : "unfrozen")} {source}", important: true);
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(LockOnDeath), nameof(LockOnDeath.Lock))]
internal static class OnPlayerLock
{
    public static void Postfix([HarmonyArgument(0)] double lockUntil)
    {
        try
        {
            FreezeLog.Record($"Player locked until {lockUntil:F0} ({OnPlayerFreezeHelpers.HoursFromNow(lockUntil):F1} game h) {OnPlayerFreezeHelpers.Source()}", important: true);
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(LockOnDeath), nameof(LockOnDeath.LockUntil))]
internal static class OnPlayerLockUntil
{
    public static void Postfix([HarmonyArgument(0)] double lockUntil, [HarmonyArgument(1)] float delay)
    {
        try
        {
            FreezeLog.Record($"Player locked until {lockUntil:F0} ({OnPlayerFreezeHelpers.HoursFromNow(lockUntil):F1} game h, delay {delay:F1}) {OnPlayerFreezeHelpers.Source()}", important: true);
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(LockOnDeath), nameof(LockOnDeath.Unlock))]
internal static class OnPlayerUnlock
{
    public static void Postfix()
    {
        try
        {
            FreezeLog.Record($"Player unlocked {OnPlayerFreezeHelpers.Source()}", important: true);
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(LockOnDeath), nameof(LockOnDeath.Freeze))]
internal static class OnPlayerLockFreeze
{
    public static void Postfix()
    {
        try
        {
            FreezeLog.Record($"Lock froze the player {OnPlayerFreezeHelpers.Source()}", important: true);
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(LockOnDeath), nameof(LockOnDeath.Unfreeze))]
internal static class OnPlayerLockUnfreeze
{
    public static void Postfix()
    {
        try
        {
            FreezeLog.Record($"Lock unfroze the player {OnPlayerFreezeHelpers.Source()}", important: true);
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(TimeDirector), nameof(TimeDirector.Pause))]
internal static class OnTimePause
{
    public static void Postfix(TimeDirector __instance)
    {
        try
        {
            FreezeLog.Record($"Time paused (pausers now {__instance._pauserCount}) {OnPlayerFreezeHelpers.Source()}");
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(TimeDirector), nameof(TimeDirector.Unpause))]
internal static class OnTimeUnpause
{
    public static void Postfix(TimeDirector __instance)
    {
        try
        {
            FreezeLog.Record($"Time unpaused (pausers now {__instance._pauserCount}) {OnPlayerFreezeHelpers.Source()}");
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(KFCCharacterController), nameof(KFCCharacterController.SetTimeOverrideEnabled))]
internal static class OnPlayerTimeOverride
{
    private static bool lastEnabled;

    public static void Postfix(KFCCharacterController __instance, [HarmonyArgument(0)] bool enabled)
    {
        try
        {
            if (enabled == lastEnabled || !OnPlayerFreezeHelpers.IsLocalPlayer(__instance))
                return;

            lastEnabled = enabled;
            FreezeLog.Record($"Player controller time override {(enabled ? "on" : "off")} {OnPlayerFreezeHelpers.Source()}");
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(TeleportablePlayer), nameof(TeleportablePlayer.TeleportTo))]
internal static class OnPlayerTeleport
{
    public static void Postfix([HarmonyArgument(0)] Vector3 position, [HarmonyArgument(1)] SceneGroup sceneGroup)
    {
        try
        {
            FreezeLog.Record($"Player teleport to {position} in {(sceneGroup ? sceneGroup.name : "this scene group")} {OnPlayerFreezeHelpers.Source()}");
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(TeleportablePlayer), nameof(TeleportablePlayer.SceneGroupChanged))]
internal static class OnPlayerSceneGroupChanged
{
    public static void Postfix([HarmonyArgument(0)] SceneGroup previous, [HarmonyArgument(1)] SceneGroup next)
    {
        try
        {
            FreezeLog.Record($"Player scene group {(previous ? previous.name : "none")} -> {(next ? next.name : "none")}");
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(CutsceneDirector), nameof(CutsceneDirector.StartCutsceneSequence))]
internal static class OnCutsceneStart
{
    public static void Postfix()
    {
        try
        {
            FreezeLog.Record($"Cutscene started {OnPlayerFreezeHelpers.Source()}");
        }
        catch
        {
            // logging only
        }
    }
}

[HarmonyPatch(typeof(CutsceneDirector), nameof(CutsceneDirector.FinishCurrentSequence))]
internal static class OnCutsceneFinish
{
    public static void Postfix()
    {
        try
        {
            FreezeLog.Record("Cutscene finished");
        }
        catch
        {
            // logging only
        }
    }
}
