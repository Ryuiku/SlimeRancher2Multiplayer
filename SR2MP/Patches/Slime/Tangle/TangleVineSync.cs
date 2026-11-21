using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime.Tangle;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Slime.Tangle;

// A tangle's grab vine for every player. Only the owner's vine grabs (GroundVineOwnerGate); its
// steps are sent so the others see a copy grow, pull in, drop and go.
[HarmonyPatch(typeof(GroundVine))]
internal static class TangleVineSync
{
    [HarmonyPatch(nameof(GroundVine.MaybeGrapple)), HarmonyPostfix]
    public static void MaybeGrapplePostfix(GroundVine __instance, bool __result)
    {
        if (!__result)
            return;

        try
        {
            NetworkTangleManager.OnVineGrappled(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle vine: announcing the vine failed: {ex.Message}");
        }
    }

    [HarmonyPatch(nameof(GroundVine.Action)), HarmonyPrefix]
    public static void ActionPrefix(GroundVine __instance, out int __state)
    {
        __state = -1;
        try
        {
            __state = (int)__instance._phase;
        }
        catch
        {
            // Left at -1: no step change is reported.
        }
    }

    [HarmonyPatch(nameof(GroundVine.Action)), HarmonyPostfix]
    public static void ActionPostfix(GroundVine __instance, int __state)
    {
        if (__state < 0)
            return;

        try
        {
            var phase = (int)__instance._phase;
            if (phase != __state)
                NetworkTangleManager.OnVinePhaseChanged(__instance, phase);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle vine: announcing a step failed: {ex.Message}");
        }
    }

    [HarmonyPatch(nameof(GroundVine.DestroyVine)), HarmonyPrefix]
    public static void DestroyVinePrefix(GroundVine __instance)
    {
        try
        {
            NetworkTangleManager.OnVineDestroyed(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle vine: announcing removal failed: {ex.Message}");
        }
    }
}
