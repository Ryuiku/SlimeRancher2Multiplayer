using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Caretaker;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Sanctuary;

// Only one game in the Sanctuary turns accumulated sprinkles into items and catches up on time
// that passed; the others show what it sends. Any error here falls back to the game's own code.
[HarmonyPatch(typeof(SprinkleCanister))]
internal static class SprinkleCanisterPatch
{
    // Runs inside the zone load, before the canister reads its model.
    [HarmonyPrefix, HarmonyPatch(nameof(SprinkleCanister.SetModel))]
    public static void SetModelPrefix(SprinkleCanisterModel model)
    {
        try
        {
            if (model != null)
                NetworkSprinkleCanisterManager.BeforeSetModel(model);
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            SrLogger.LogWarning($"Could not prepare a sprinkle canister: {ex.Message}");
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(SprinkleCanister.SetModel))]
    public static void SetModelPostfix(SprinkleCanister __instance)
    {
        try
        {
            NetworkSprinkleCanisterManager.AfterSetModel(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not register a sprinkle canister: {ex.Message}");
        }
    }

    // The one place where accumulated sprinkles become items in the canister.
    [HarmonyPrefix, HarmonyPatch(nameof(SprinkleCanister.ConsumeAccumulatedResources))]
    public static bool ConsumePrefix(ref int __result)
    {
        try
        {
            if (!NetworkSprinkleCanisterManager.InSession || NetworkSprinkleCanisterManager.IsLocalProducer())
                return true;

            __result = 0;
            return false;
        }
        catch
        {
            return true;
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(SprinkleCanister.ConsumeAccumulatedResources))]
    public static void ConsumePostfix(SprinkleCanister __instance, int __result)
    {
        if (__result <= 0 || !NetworkSprinkleCanisterManager.InSession)
            return;

        try
        {
            NetworkSprinkleCanisterManager.MarkDirty(NetworkSprinkleCanisterManager.CanisterId(__instance));
        }
        catch
        {
            // ignored
        }
    }

    // Catching up on time that passed while nobody was here: only the producer does it, once.
    [HarmonyPrefix, HarmonyPatch(nameof(SprinkleCanister.FastForward))]
    public static bool FastForwardPrefix() => ShouldRunCatchUp();

    [HarmonyPrefix, HarmonyPatch(nameof(SprinkleCanister.UpdateToTime))]
    public static bool UpdateToTimePrefix() => ShouldRunCatchUp();

    [HarmonyPostfix, HarmonyPatch(nameof(SprinkleCanister.Tick))]
    public static void TickPostfix(SprinkleCanister __instance)
    {
        try
        {
            NetworkSprinkleCanisterManager.AfterTick(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not share a sprinkle canister: {ex.Message}");
        }
    }

    private static bool ShouldRunCatchUp()
    {
        try
        {
            return !NetworkSprinkleCanisterManager.InSession || NetworkSprinkleCanisterManager.IsLocalProducer();
        }
        catch
        {
            return true;
        }
    }
}
