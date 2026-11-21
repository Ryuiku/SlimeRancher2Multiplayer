using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime.Slumber;
using SR2MP.Components.Actor;

namespace SR2MP.Patches.Slime.Sloomber;

// Every player's copy of a sloomber used to yawn on its own schedule as well as mirroring the
// owner's yawn, so nearby slimes got sleepy more than once and extra clouds showed. Only the owner
// yawns now; the others show the owner's yawn from SloomberYawn packets.
[HarmonyPatch(typeof(InitiateYawn))]
internal static class InitiateYawnOwnerGate
{
    [HarmonyPatch(nameof(InitiateYawn.Selected)), HarmonyPrefix]
    public static bool SelectedPrefix(InitiateYawn __instance)
        => SubbehaviourOwnerGate.SelectedPrefix(__instance);

    [HarmonyPatch(nameof(InitiateYawn.Selected)), HarmonyPostfix]
    public static void SelectedPostfix(InitiateYawn __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.SelectedPostfix(__instance, __runOriginal);

    [HarmonyPatch(nameof(InitiateYawn.Action)), HarmonyPrefix]
    public static bool ActionPrefix(InitiateYawn __instance)
        => SubbehaviourOwnerGate.ActionPrefix(__instance);

    [HarmonyPatch(nameof(InitiateYawn.Deselected)), HarmonyPrefix]
    public static bool DeselectedPrefix(InitiateYawn __instance)
        => SubbehaviourOwnerGate.DeselectedPrefix(__instance);

    [HarmonyPatch(nameof(InitiateYawn.Deselected)), HarmonyPostfix]
    public static void DeselectedPostfix(InitiateYawn __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.DeselectedPostfix(__instance, __runOriginal);

    // The yawn itself (cloud and sleepiness around it) only on the owner's game, or when a packet
    // shows the owner's yawn here.
    [HarmonyPatch(nameof(InitiateYawn.DoYawn)), HarmonyPrefix]
    public static bool DoYawnPrefix(InitiateYawn __instance)
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
            SrLogger.LogDebug($"InitiateYawn.DoYawn gate failed: {exception.Message}");
            return true;
        }
    }
}
