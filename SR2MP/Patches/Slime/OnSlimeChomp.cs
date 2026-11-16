using HarmonyLib;
using SR2MP.Components.Actor;

namespace SR2MP.Patches.Slime;

// Only the player simulating a slime lets it eat. Every player's game ran the eating logic for every
// slime, so food and plorts got eaten (and deleted for everyone) by copies of slimes on other players'
// machines, and largo transformations ran more than once.
[HarmonyPatch(typeof(SlimeEat))]
internal static class OnSlimeChomp
{
    private static bool MayEat(SlimeEat slimeEat, GameObject? target = null)
    {
        if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected))
            return true;

        var networkActor = slimeEat.GetComponent<NetworkActor>();
        if (!networkActor || networkActor.LocallyOwned)
            return true;

        return TryTakeOverForFeeding(slimeEat, networkActor, target);
    }

    // Food we just shot at a slime another player is simulating. Whether a slime accepts food shot
    // at it depends on things only the shooter's game knows (that it was shot, and how), so the
    // slime becomes ours and the bite happens here, the same as in single player.
    private static bool TryTakeOverForFeeding(SlimeEat slimeEat, NetworkActor eater, GameObject? target)
    {
        try
        {
            if (!target || eater.RecentlyClaimedByOtherPlayer)
                return false;

            var targetActor = target!.GetComponentInParent<NetworkActor>();
            if (!targetActor || !targetActor.LocallyOwned)
                return false;

            var vacuumable = target.GetComponentInParent<Vacuumable>();
            if (!vacuumable || !vacuumable.IsLaunched())
                return false;

            if (!slimeEat.DoesEat(target))
                return false;

            return SR2MP.Patches.Actor.OnActorVacced.Claim(eater);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"OnSlimeChomp feeding takeover failed: {exception.Message}");
            return false;
        }
    }

    [HarmonyPatch(nameof(SlimeEat.MaybeChomp)), HarmonyPrefix]
    public static bool MaybeChompPrefix(SlimeEat __instance, [HarmonyArgument(0)] GameObject target, ref bool __result)
    {
        if (MayEat(__instance, target))
            return true;

        __result = false;
        return false;
    }

    [HarmonyPatch(nameof(SlimeEat.MaybeSpinAndChomp)), HarmonyPrefix]
    public static bool MaybeSpinAndChompPrefix(SlimeEat __instance, [HarmonyArgument(0)] GameObject target, ref bool __result)
    {
        if (MayEat(__instance, target))
            return true;

        __result = false;
        return false;
    }

    // The eaten thing is destroyed by the eater's game, and a game may only destroy slimes and food
    // it owns. So when one of our slimes starts a bite, the target becomes ours too. Without this a
    // slime could eat another player's carrot (or a tarr another player's slime) without it ever
    // disappearing: endless plorts from one carrot, a new tarr per bite with no slime lost.
    [HarmonyPatch(nameof(SlimeEat.MaybeChomp)), HarmonyPostfix]
    public static void MaybeChompPostfix(SlimeEat __instance, [HarmonyArgument(0)] GameObject target, bool __result)
    {
        if (__result)
            TryOwnTarget(__instance, target);
    }

    [HarmonyPatch(nameof(SlimeEat.MaybeSpinAndChomp)), HarmonyPostfix]
    public static void MaybeSpinAndChompPostfix(SlimeEat __instance, [HarmonyArgument(0)] GameObject target, bool __result)
    {
        if (__result)
            TryOwnTarget(__instance, target);
    }

    // The bite lands. The slime or its target may have changed hands during the bite animation
    // (another player swept a vac over them). If we no longer own the slime its new owner decides,
    // and if the target can't be made ours the bite does nothing. Finishing it anyway transformed
    // the slime here while the original stayed for everyone else.
    [HarmonyPatch(nameof(SlimeEat.FinishChomp)), HarmonyPrefix]
    public static bool FinishChompPrefix(SlimeEat __instance, [HarmonyArgument(0)] GameObject target)
        => MayEat(__instance) && TryOwnTarget(__instance, target);

    [HarmonyPatch(nameof(SlimeEat.EatImmediate), typeof(GameObject), typeof(IdentifiableType),
        typeof(Il2CppSystem.Collections.Generic.List<IdentifiableType>),
        typeof(Il2CppSystem.Collections.Generic.List<IdentifiableType>), typeof(bool)), HarmonyPrefix]
    public static bool EatImmediatePrefix(SlimeEat __instance, [HarmonyArgument(0)] GameObject target)
        => MayEatImmediate(__instance, target);

    [HarmonyPatch(nameof(SlimeEat.EatImmediate), typeof(GameObject), typeof(IdentifiableType),
        typeof(Il2CppSystem.Collections.Generic.List<IdentifiableType>),
        typeof(Il2CppSystem.Collections.Generic.List<IdentifiableType>), typeof(bool), typeof(double)), HarmonyPrefix]
    public static bool EatImmediateTimedPrefix(SlimeEat __instance, [HarmonyArgument(0)] GameObject target)
        => MayEatImmediate(__instance, target);

    // EatImmediate without a food object is the game feeding corral slimes from feeders for the
    // time a ranch area was asleep. A slime that was asleep with its area is ours again only a
    // frame after it wakes, so the strict check would silently skip that feeding (no plorts for
    // the time away). It is allowed unless another player is simulating the slime right now.
    private static bool MayEatImmediate(SlimeEat slimeEat, GameObject target)
    {
        if (target)
            return MayEat(slimeEat) && TryOwnTarget(slimeEat, target);

        if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected))
            return true;

        try
        {
            var eater = slimeEat.GetComponent<NetworkActor>();
            if (!eater || eater.LocallyOwned)
                return true;

            eater.EnsureOwnerInfo();
            return !eater.IsActivelyOwnedByOtherPlayer;
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"OnSlimeChomp catch-up check failed: {exception.Message}");
            return true;
        }
    }

    // True if the bite may go ahead: the target is ours, became ours, or isn't a synced actor at all.
    private static bool TryOwnTarget(SlimeEat slimeEat, GameObject target)
    {
        try
        {
            if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected))
                return true;

            if (!target)
                return true;

            var eater = slimeEat.GetComponent<NetworkActor>();
            if (eater && !eater.LocallyOwned)
                return false;

            var targetActor = target.GetComponentInParent<NetworkActor>();
            if (!targetActor || targetActor.LocallyOwned)
                return true;

            // Another player just grabbed it or is carrying it.
            if (targetActor.RecentlyClaimedByOtherPlayer)
                return false;

            if (!SR2MP.Shared.Managers.NetworkActorManager.CanSendActorChanges)
                return false;

            if (targetActor.ActorId.Value == 0)
                return true;

            return SR2MP.Patches.Actor.OnActorVacced.Claim(targetActor);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"OnSlimeChomp failed: {exception.Message}");
            return true;
        }
    }
}
