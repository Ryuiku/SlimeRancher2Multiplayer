using HarmonyLib;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.LandPlots;

/// <summary>
/// Ranch catch-up: tracks the ranch areas, moves an area's asleep-since time right before it
/// wakes, and lets only the player who simulates an area run its catch-up. Drones, slime moods
/// and sprinkle canisters are left to the game: drones are handled on their own, moods follow
/// each slime's owner, and canisters already have one producer (NetworkSprinkleCanisterManager).
/// </summary>
[HarmonyPatch(typeof(RanchCellFastForwarder))]
internal static class RanchCellPatches
{
    [HarmonyPostfix, HarmonyPatch(nameof(RanchCellFastForwarder.Start))]
    public static void StartPostfix(RanchCellFastForwarder __instance)
        => NetworkRanchCellManager.Track(__instance);

    [HarmonyPrefix, HarmonyPatch(nameof(RanchCellFastForwarder.OnDestroy))]
    public static void OnDestroyPrefix(RanchCellFastForwarder __instance)
        => NetworkRanchCellManager.Untrack(__instance);

    [HarmonyPrefix, HarmonyPatch(nameof(RanchCellFastForwarder.OnHibernationStateChanged))]
    public static void OnHibernationStateChangedPrefix(RanchCellFastForwarder __instance, [HarmonyArgument(0)] bool hibernating)
        => NetworkRanchCellManager.OnHibernationChanging(__instance, hibernating);

    // The argument names of these are not known; the first two are taken as the time window, which
    // is only used for the log. A held part is repeated later with the same arguments.

    [HarmonyPrefix, HarmonyPatch(nameof(RanchCellFastForwarder.FastForwardCorrals))]
    public static bool FastForwardCorralsPrefix(RanchCellFastForwarder __instance, double __0, double __1, double __2, out bool __state)
    {
        var cell = __instance;
        __state = !NetworkRanchCellManager.ShouldSkipCatchUp(__instance, "corral", __0, __1,
            () => cell.FastForwardCorrals(__0, __1, __2));

        // Lets the feeding through for slimes another player owns (see OnSlimeChomp).
        if (__state)
            NetworkRanchCellManager.BeginCatchUp();
        return __state;
    }

    [HarmonyPostfix, HarmonyPatch(nameof(RanchCellFastForwarder.FastForwardCorrals))]
    public static void FastForwardCorralsPostfix(bool __state)
    {
        if (__state)
            NetworkRanchCellManager.EndCatchUp();
    }

    [HarmonyPrefix, HarmonyPatch(nameof(RanchCellFastForwarder.FastForwardPonds))]
    public static bool FastForwardPondsPrefix(RanchCellFastForwarder __instance, double __0, double __1, double __2, out bool __state)
    {
        var cell = __instance;
        __state = !NetworkRanchCellManager.ShouldSkipCatchUp(__instance, "pond", __0, __1,
            () => cell.FastForwardPonds(__0, __1, __2));

        if (__state)
            NetworkRanchCellManager.BeginCatchUp();
        return __state;
    }

    [HarmonyPostfix, HarmonyPatch(nameof(RanchCellFastForwarder.FastForwardPonds))]
    public static void FastForwardPondsPostfix(bool __state)
    {
        if (__state)
            NetworkRanchCellManager.EndCatchUp();
    }

    [HarmonyPrefix, HarmonyPatch(nameof(RanchCellFastForwarder.FastForwardIncinerators))]
    public static bool FastForwardIncineratorsPrefix(RanchCellFastForwarder __instance, double __0, double __1, double __2)
    {
        var cell = __instance;
        return !NetworkRanchCellManager.ShouldSkipCatchUp(__instance, "incinerator", __0, __1,
            () => cell.FastForwardIncinerators(__0, __1, __2));
    }

    [HarmonyPrefix, HarmonyPatch(nameof(RanchCellFastForwarder.FastForwardGardens))]
    public static bool FastForwardGardensPrefix(RanchCellFastForwarder __instance, double __0, double __1, float __2)
    {
        var cell = __instance;
        return !NetworkRanchCellManager.ShouldSkipCatchUp(__instance, "garden", __0, __1,
            () => cell.FastForwardGardens(__0, __1, __2));
    }
}

/// <summary>
/// Only the player who simulates a ranch area runs its auto-feeders. Two games at the same corral
/// both ejected food and both took it from the feeder. The driver's ejects, storage changes and
/// schedule are sent to the others.
/// </summary>
[HarmonyPatch(typeof(SlimeFeeder), nameof(SlimeFeeder.FixedUpdate))]
internal static class OnFeederFixedUpdate
{
    public static bool Prefix(SlimeFeeder __instance)
    {
        try
        {
            return !NetworkRanchCellManager.IsOtherSimulator(__instance._region);
        }
        catch
        {
            return true;
        }
    }
}
