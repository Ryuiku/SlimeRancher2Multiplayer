using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Labyrinth;

[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.OnFilledChangedFromModel))]
internal static class OnPlortDepositorDeposit
{
    public static void Postfix(PlortDepositor __instance)
    {
        if (HandlingPacket) return;

        NetworkPlortDepositorManager.Broadcast(__instance);
    }
}

[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.OnFilledChanged))]
internal static class OnPlortDepositorFilledChanged
{
    public static void Postfix(PlortDepositor __instance)
    {
        if (HandlingPacket) return;

        NetworkPlortDepositorManager.Broadcast(__instance);
    }
}

// A plort going in. The two patches above only run when the game calls those methods, which it does
// not appear to do for a deposit made during play, so shadow plort doors stayed shut for everyone
// but the player who fed them. This one compares the amount before and after the plort touched the
// depositor and tells the others whenever it went up.
[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.OnTriggerEnter))]
internal static class OnPlortDepositorTrigger
{
    public static bool Prefix(PlortDepositor __instance, [HarmonyArgument(0)] Collider collider, out int __state)
    {
        __state = -1;

        try
        {
            __state = __instance._model?.AmountDeposited ?? -1;

            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                return true;

            // Every player's game sees the same plort fly in. Only the player simulating it counts
            // it, otherwise one plort could be counted once per player.
            if (collider)
            {
                var networkActor = collider.GetComponentInParent<SR2MP.Components.Actor.NetworkActor>();
                if (networkActor && !networkActor.LocallyOwned)
                    return false;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"OnPlortDepositorTrigger prefix failed: {ex.Message}");
        }

        return true;
    }

    public static void Postfix(PlortDepositor __instance, int __state)
    {
        if (HandlingPacket) return;
        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        try
        {
            var model = __instance._model;
            if (model == null || __state < 0 || model.AmountDeposited == __state)
                return;

            NetworkPlortDepositorManager.Broadcast(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"OnPlortDepositorTrigger postfix failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.ActivateOnFill))]
internal static class OnPlortDepositorActivateOnFill
{
    public static void Postfix(PlortDepositor __instance)
    {
        if (HandlingPacket) return;
        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        NetworkPlortDepositorManager.Broadcast(__instance);
    }
}

[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.Awake))]
internal static class OnPlortDepositorAwake
{
    public static void Postfix(PlortDepositor __instance)
        => NetworkPlortDepositorManager.ApplyPendingState(__instance, __instance._model);
}

[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.InitModel))]
internal static class OnPlortDepositorModelInit
{
    public static void Postfix(PlortDepositor __instance, PlortDepositorModel model)
        => NetworkPlortDepositorManager.ApplyPendingState(__instance, model);
}

[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.SetModel))]
internal static class OnPlortDepositorModelSet
{
    public static void Postfix(PlortDepositor __instance, PlortDepositorModel model)
        => NetworkPlortDepositorManager.ApplyPendingState(__instance, model);
}
