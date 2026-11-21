using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime;

namespace SR2MP.Patches.Slime.Tabby;

// Only the tabby's owner runs its stalking; the others are shown it through SlimeStalk packets.
[HarmonyPatch(typeof(StalkConsumable))]
internal static class StalkConsumableOwnerGate
{
    [HarmonyPatch(nameof(StalkConsumable.Selected)), HarmonyPrefix]
    public static bool SelectedPrefix(StalkConsumable __instance)
        => SubbehaviourOwnerGate.SelectedPrefix(__instance);

    [HarmonyPatch(nameof(StalkConsumable.Selected)), HarmonyPostfix]
    public static void SelectedPostfix(StalkConsumable __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.SelectedPostfix(__instance, __runOriginal);

    [HarmonyPatch(nameof(StalkConsumable.Action)), HarmonyPrefix]
    public static bool ActionPrefix(StalkConsumable __instance)
        => SubbehaviourOwnerGate.ActionPrefix(__instance);

    [HarmonyPatch(nameof(StalkConsumable.Deselected)), HarmonyPrefix]
    public static bool DeselectedPrefix(StalkConsumable __instance)
        => SubbehaviourOwnerGate.DeselectedPrefix(__instance);

    [HarmonyPatch(nameof(StalkConsumable.Deselected)), HarmonyPostfix]
    public static void DeselectedPostfix(StalkConsumable __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.DeselectedPostfix(__instance, __runOriginal);
}
