using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime.Tangle;
using SR2MP.Components.Actor;

namespace SR2MP.Patches.Slime.Tangle;

// Every player's copy of a tangle used to roll for and grow its own bud stalks. The bud is an actor,
// so each copy's bud was shared with everyone: one bud per player per stalk, the others' hanging in
// the air with no stalk. Only the tangle's owner grows buds now.
[HarmonyPatch(typeof(TangleSpawnBud))]
internal static class TangleSpawnBudOwnerGate
{
    [HarmonyPatch(nameof(TangleSpawnBud.Selected)), HarmonyPrefix]
    public static bool SelectedPrefix(TangleSpawnBud __instance)
        => SubbehaviourOwnerGate.SelectedPrefix(__instance);

    [HarmonyPatch(nameof(TangleSpawnBud.Selected)), HarmonyPostfix]
    public static void SelectedPostfix(TangleSpawnBud __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.SelectedPostfix(__instance, __runOriginal);

    [HarmonyPatch(nameof(TangleSpawnBud.Action)), HarmonyPrefix]
    public static bool ActionPrefix(TangleSpawnBud __instance)
        => SubbehaviourOwnerGate.ActionPrefix(__instance);

    [HarmonyPatch(nameof(TangleSpawnBud.Deselected)), HarmonyPrefix]
    public static bool DeselectedPrefix(TangleSpawnBud __instance)
        => SubbehaviourOwnerGate.DeselectedPrefix(__instance);

    [HarmonyPatch(nameof(TangleSpawnBud.Deselected)), HarmonyPostfix]
    public static void DeselectedPostfix(TangleSpawnBud __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.DeselectedPostfix(__instance, __runOriginal);

    // Also blocked on its own, in case the game grows a stalk from somewhere other than Action.
    [HarmonyPatch(nameof(TangleSpawnBud.SpawnBudWithStalk)), HarmonyPrefix]
    public static bool SpawnBudWithStalkPrefix(TangleSpawnBud __instance)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected) return true;
            if (HandlingPacket) return true;

            var networkActor = __instance.GetComponent<NetworkActor>();
            return !networkActor || networkActor.LocallyOwned;
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"TangleSpawnBud.SpawnBudWithStalk gate failed: {exception.Message}");
            return true;
        }
    }
}
