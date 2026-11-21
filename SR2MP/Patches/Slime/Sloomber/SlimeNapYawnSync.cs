using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.Slime.Slumber;

namespace SR2MP.Patches.Slime.Sloomber;

// A napping sloomber yawns in its sleep through SlimeNap.Action, its own copy of the yawn (animation
// and head cloud, face, then a direct InitiateYawn.DoYawn call) that never runs InitiateYawn.Selected
// or Action, so no wind-up was sent and the others only saw the cloud. The cheat's yawns on napping
// sloombers take this path too. Copies run by another player have their nap yawn held back by the
// prefix before it starts, and their owner's yawn arrives as a packet. The postfix's undo of a yawn a
// copy started anyway is only a fallback.
// SlimeNap runs on many slimes every physics tick, so the postfix returns at once unless the yawn
// state changed. SlimeNap.Action and Deselected are not shared code (one RVA each).
[HarmonyPatch(typeof(SlimeNap))]
internal static class SlimeNapYawnSync
{
    private static bool loggedError;

    private static void LogOnce(string where, Exception exception)
    {
        if (loggedError)
            return;

        loggedError = true;
        SrLogger.LogDebug($"Sloomber nap yawn sync ({where}) failed: {exception.Message}");
    }

    [HarmonyPatch(nameof(SlimeNap.Action)), HarmonyPrefix]
    public static void ActionPrefix(SlimeNap __instance, out SlimeNap.YawnState __state)
    {
        try
        {
            __state = __instance._yawnState;
        }
        catch (Exception exception)
        {
            LogOnce("read", exception);
            __state = SlimeNap.YawnState.IDLE;
            return;
        }

        try
        {
            // Another player's sloomber: push this copy's own nap yawn back before the game starts it,
            // so it never begins or ends the head cloud a yawn received from the owner is showing.
            if (__state != SlimeNap.YawnState.IDLE)
                return;

            var yawn = __instance._initiateYawn;
            if (!yawn || !SceneContext.Instance.TimeDirector.HasReached(yawn!._cooldownUntilTime))
                return;

            if (!SubbehaviourOwnerGate.MayRun(__instance))
                yawn.ResetCooldown(1f);
        }
        catch (Exception exception)
        {
            LogOnce("hold", exception);
        }
    }

    [HarmonyPatch(nameof(SlimeNap.Action)), HarmonyPostfix]
    public static void ActionPostfix(SlimeNap __instance, SlimeNap.YawnState __state)
    {
        try
        {
            var now = __instance._yawnState;
            if (now == __state)
                return;

            var yawn = __instance._initiateYawn;
            if (!yawn)
                return;

            var started = __state == SlimeNap.YawnState.IDLE && now == SlimeNap.YawnState.BEGIN_YAWN;

            if (SubbehaviourOwnerGate.MayRun(__instance))
            {
                InitiateYawnWindUp.Note(yawn!, now == SlimeNap.YawnState.IDLE,
                    now is SlimeNap.YawnState.BEGIN_YAWN or SlimeNap.YawnState.BEGIN_YAWN_FACE, started);
                return;
            }

            // Another player's sloomber: its owner yawns and sends it. The prefix normally holds this copy's
            // yawn back; as a fallback, undo the yawn this copy just started.
            if (started)
            {
                __instance._yawnState = SlimeNap.YawnState.IDLE;

                var animator = __instance._animator;
                if (animator)
                    animator!.ResetTrigger(__instance._animYawnId);

                var reactables = __instance._yawnComponentReactables;
                if (reactables != null)
                {
                    foreach (var reactable in reactables)
                        reactable?.EndYawn();
                }

                yawn!.ResetCooldown(1f);
            }
        }
        catch (Exception exception)
        {
            LogOnce("Action", exception);
        }
    }

    // Woke up during a nap yawn: cancel the wind-up the others are showing and forget the cycle.
    // SlimeNap.Deselected leaves _yawnState as it was (only Selected sets it back to IDLE).
    [HarmonyPatch(nameof(SlimeNap.Deselected)), HarmonyPostfix]
    public static void DeselectedPostfix(SlimeNap __instance)
    {
        try
        {
            if (__instance._yawnState == SlimeNap.YawnState.IDLE)
                return;

            var yawn = __instance._initiateYawn;
            if (yawn)
                InitiateYawnWindUp.NoteStopped(yawn!);
        }
        catch (Exception exception)
        {
            LogOnce("Deselected", exception);
        }
    }
}
