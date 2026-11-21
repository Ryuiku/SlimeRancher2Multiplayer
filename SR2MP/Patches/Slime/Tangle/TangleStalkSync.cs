using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime.Tangle;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Slime.Tangle;

// Tangle bud stalks for every player. The game that grows a stalk tells the others, who show a copy
// that grows and withers with it. The bud is a shared actor already, so a copy never grows its own.
// Each patched method was checked to have native code of its own (OnAnimatorOutComplete and Despawn
// share theirs, so neither is patched).
[HarmonyPatch(typeof(VineBulbWithStalk))]
internal static class TangleStalkSync
{
    [HarmonyPatch(nameof(VineBulbWithStalk.Start)), HarmonyPostfix]
    public static void StartPostfix(VineBulbWithStalk __instance)
    {
        try
        {
            NetworkTangleManager.OnStalkStarted(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle stalk: announcing a stalk failed: {ex.Message}");
        }
    }

    // A copy never hangs a bud: the grower's bud reaches this game as a shared actor.
    [HarmonyPatch(nameof(VineBulbWithStalk.Spawn)), HarmonyPrefix]
    public static bool SpawnPrefix(VineBulbWithStalk __instance)
    {
        try
        {
            return !NetworkTangleManager.IsRemoteCopy(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle stalk: Spawn gate failed: {ex.Message}");
            return true;
        }
    }

    [HarmonyPatch(nameof(VineBulbWithStalk.Spawn)), HarmonyPostfix]
    public static void SpawnPostfix(VineBulbWithStalk __instance, bool __runOriginal)
    {
        if (!__runOriginal)
            return;

        try
        {
            NetworkTangleManager.OnStalkSpawnedBud(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle stalk: remembering its bud failed: {ex.Message}");
        }
    }

    // A copy has no bud to let go of.
    [HarmonyPatch(nameof(VineBulbWithStalk.OnDetached)), HarmonyPrefix]
    public static bool OnDetachedPrefix(VineBulbWithStalk __instance)
    {
        try
        {
            return !NetworkTangleManager.IsRemoteCopy(__instance);
        }
        catch
        {
            return true;
        }
    }

    [HarmonyPatch(nameof(VineBulbWithStalk.TriggerAnimatorOut)), HarmonyPrefix]
    public static void TriggerAnimatorOutPrefix(VineBulbWithStalk __instance, [HarmonyArgument(0)] bool silent)
    {
        try
        {
            NetworkTangleManager.OnStalkOut(__instance, silent);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle stalk: announcing withering failed: {ex.Message}");
        }
    }

    [HarmonyPatch(nameof(VineBulbWithStalk.OnDestroy)), HarmonyPostfix]
    public static void OnDestroyPostfix(VineBulbWithStalk __instance)
    {
        try
        {
            NetworkTangleManager.OnStalkDestroyed(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle stalk: cleanup failed: {ex.Message}");
        }
    }
}

// The bud on a stalk this game grew, once another player took it.
[HarmonyPatch(typeof(VacBudOnStalk), nameof(VacBudOnStalk.Update))]
internal static class TangleBudTakenByOther
{
    public static void Postfix(VacBudOnStalk __instance)
    {
        try
        {
            NetworkTangleManager.CheckLocalBud(__instance.gameObject);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle bud: ownership check failed: {ex.Message}");
        }
    }
}
