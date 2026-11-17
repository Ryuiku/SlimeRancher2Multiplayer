using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime.Tangle;
using SR2MP.Components.Actor;
using SR2MP.Patches.Actor;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Slime.Tangle;

// Only the tangle's owner fires its grab vine. A copy on another player's game could grab food,
// but only the owner's slime may eat, so the food was held and never eaten.
[HarmonyPatch(typeof(GroundVine))]
internal static class GroundVineOwnerGate
{
    [HarmonyPatch(nameof(GroundVine.Selected)), HarmonyPrefix]
    public static bool SelectedPrefix(GroundVine __instance)
        => SubbehaviourOwnerGate.SelectedPrefix(__instance);

    [HarmonyPatch(nameof(GroundVine.Selected)), HarmonyPostfix]
    public static void SelectedPostfix(GroundVine __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.SelectedPostfix(__instance, __runOriginal);

    [HarmonyPatch(nameof(GroundVine.Action)), HarmonyPrefix]
    public static bool ActionPrefix(GroundVine __instance)
        => SubbehaviourOwnerGate.ActionPrefix(__instance, LetGo);

    [HarmonyPatch(nameof(GroundVine.Deselected)), HarmonyPrefix]
    public static bool DeselectedPrefix(GroundVine __instance)
        => SubbehaviourOwnerGate.DeselectedPrefix(__instance);

    [HarmonyPatch(nameof(GroundVine.Deselected)), HarmonyPostfix]
    public static void DeselectedPostfix(GroundVine __instance, bool __runOriginal)
        => SubbehaviourOwnerGate.DeselectedPostfix(__instance, __runOriginal);

    // Another player took the tangle while its vine held something. The game's Deselected should
    // already have let go; make sure nothing stays tied to a tangle this game no longer moves.
    private static void LetGo(Subbehaviour behaviour)
    {
        var vine = behaviour.TryCast<GroundVine>();
        if (vine == null)
            return;

        try
        {
            if (vine._joint)
                vine.Release();
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"GroundVine release after losing ownership failed: {exception.Message}");
        }

        try
        {
            if (vine._activeVine)
                vine.DestroyVine();
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"GroundVine vine removal after losing ownership failed: {exception.Message}");
        }
    }

    // The vine pulls its catch to the tangle, which then eats it. That only works on food this
    // game owns, so the catch becomes ours first; food another player is holding is left alone.
    // The same rule as a slime biting something (OnSlimeChomp).
    [HarmonyPatch(nameof(GroundVine.MaybeGrapple)), HarmonyPrefix]
    public static bool MaybeGrapplePrefix(GroundVine __instance, [HarmonyArgument(0)] GameObject target, ref bool __result)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected) return true;
            if (HandlingPacket) return true;

            var tangle = __instance.GetComponent<NetworkActor>();
            if (tangle && !tangle.LocallyOwned)
            {
                __result = false;
                return false;
            }

            if (!target)
                return true;

            var targetActor = target.GetComponentInParent<NetworkActor>();
            if (!targetActor || targetActor.LocallyOwned)
                return true;

            if (targetActor.RecentlyClaimedByOtherPlayer || !NetworkActorManager.CanSendActorChanges)
            {
                __result = false;
                return false;
            }

            if (OnActorVacced.Claim(targetActor))
                return true;

            __result = false;
            return false;
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"GroundVine.MaybeGrapple gate failed: {exception.Message}");
            return true;
        }
    }
}
