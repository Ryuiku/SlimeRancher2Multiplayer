using System.Collections;
using System.Net;
using Il2CppMonomiPark.SlimeRancher.Slime.Slumber;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Slime.Sloomber;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Slime.Sloomber;

// Shows another player's sloomber yawning: the wind-up (yawn animation, the cloud on its head
// fading), the yawn itself, or the wind-up ending without a yawn. The copy here does not yawn on
// its own (only the owner's does), so this is all the others see.
[PacketHandler((byte)PacketType.SloomberYawn)]
internal sealed class SloomberYawnHandler : BasePacketHandler<SloomberYawnPacket>
{
    // A wind-up whose yawn or cancel never comes (the owner lost the slime halfway) is ended after
    // the wind-up's own length plus this.
    private const float WindUpGrace = 5f;

    // A yawn that follows a wind-up shown less than this long ago belongs to that wind-up.
    private const float WindUpMatchTime = 15f;

    // Whether the yawn animator parameter is a bool or a trigger, by its hash.
    private static readonly Dictionary<int, AnimatorControllerParameterType> ParameterTypes = new();

    // When the wind-up was shown for each sloomber, and a counter that tells a running safety
    // timer whether its wind-up is still the current one.
    private static readonly Dictionary<long, float> WindUpShown = new();
    private static readonly Dictionary<long, int> WindUpVersion = new();

    protected override bool Handle(SloomberYawnPacket packet, IPEndPoint? _)
    {
        if (!ActorManager.Actors.TryGetValue(packet.ActorId.Value, out var model))
            return true;

        if (!model.TryGetNetworkComponent(out var networkComponent))
            return true;

        // A late packet from the previous owner: this game runs the sloomber now.
        if (networkComponent.LocallyOwned)
            return true;

        var yawn = networkComponent.GetComponent<InitiateYawn>();
        if (!yawn)
            return true;

        var actorId = packet.ActorId.Value;

        HandlingPacket = true;
        try
        {
            if (packet.Active && !packet.Cancel)
                BeginWindUp(yawn, actorId);
            else if (packet.Cancel)
                CancelWindUp(yawn, actorId);
            else
                Yawn(yawn, actorId);
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }

    private static void BeginWindUp(InitiateYawn yawn, long actorId)
    {
        try
        {
            foreach (var reactable in yawn._yawnComponentReactables)
                reactable.BeginYawn();
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: BeginYawn failed: {exception.Message}");
        }

        try
        {
            if (TryGetAnimation(yawn, out var animator, out var id, out var type))
            {
                if (type == AnimatorControllerParameterType.Trigger)
                    animator.SetTrigger(id);
                else if (type == AnimatorControllerParameterType.Bool)
                    animator.SetBool(id, true);
            }
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: wind-up animation failed: {exception.Message}");
        }

        WindUpShown[actorId] = UnityEngine.Time.time;
        var version = NextVersion(actorId);

        var waitTime = WindUpGrace;
        try
        {
            var metadata = yawn.InitiateYawnMetadata;
            if (metadata)
                waitTime += Mathf.Max(0f, metadata.YawnStartFaceTime) + Mathf.Max(0f, metadata.TimeUntilCloud);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: reading the yawn timings failed: {exception.Message}");
        }

        StartCoroutine(EndWindUpIfAbandoned(yawn, actorId, version, waitTime));
    }

    private static void CancelWindUp(InitiateYawn yawn, long actorId)
    {
        WindUpShown.Remove(actorId);
        NextVersion(actorId);

        EndReactables(yawn);

        try
        {
            ResetAnimation(yawn);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: ending the wind-up animation failed: {exception.Message}");
        }
    }

    private static void Yawn(InitiateYawn yawn, long actorId)
    {
        var hadWindUp = WindUpShown.Remove(actorId, out var shownAt)
                        && UnityEngine.Time.time - shownAt < WindUpMatchTime;
        NextVersion(actorId);

        try
        {
            yawn.DoYawn();
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: DoYawn failed: {exception.Message}");
        }

        EndReactables(yawn);

        if (!hadWindUp)
            return;

        try
        {
            if (!TryGetAnimation(yawn, out var animator, out var id, out var type))
                return;

            // The wind-up already started the animation; if DoYawn set the trigger again it would
            // play a second time.
            if (type == AnimatorControllerParameterType.Trigger)
            {
                animator.ResetTrigger(id);
            }
            else if (type == AnimatorControllerParameterType.Bool)
            {
                var recoverTime = 1f;
                var metadata = yawn.InitiateYawnMetadata;
                if (metadata)
                    recoverTime = metadata.RecoverTime;

                StartCoroutine(EndBoolLater(yawn, actorId, Mathf.Clamp(recoverTime, 0.5f, 5f)));
            }
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: ending the yawn animation failed: {exception.Message}");
        }
    }

    private static void EndReactables(InitiateYawn yawn)
    {
        try
        {
            foreach (var reactable in yawn._yawnComponentReactables)
                reactable.EndYawn();
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: EndYawn failed: {exception.Message}");
        }
    }

    private static int NextVersion(long actorId)
    {
        var version = WindUpVersion.TryGetValue(actorId, out var current) ? current + 1 : 1;
        WindUpVersion[actorId] = version;
        return version;
    }

    private static bool IsCurrent(long actorId, int version)
        => WindUpVersion.TryGetValue(actorId, out var current) && current == version;

    private static IEnumerator EndWindUpIfAbandoned(InitiateYawn yawn, long actorId, int version, float waitTime)
    {
        yield return new WaitForSeconds(waitTime);

        if (!IsCurrent(actorId, version) || !yawn)
            yield break;

        WindUpShown.Remove(actorId);

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            EndReactables(yawn);
            ResetAnimation(yawn);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: ending an abandoned wind-up failed: {exception.Message}");
        }
        finally
        {
            HandlingPacket = wasHandling;
        }
    }

    private static IEnumerator EndBoolLater(InitiateYawn yawn, long actorId, float delay)
    {
        var version = WindUpVersion.TryGetValue(actorId, out var current) ? current : 0;

        yield return new WaitForSeconds(delay);

        // A new wind-up started meanwhile: leave its animation alone.
        if (!IsCurrent(actorId, version) || !yawn)
            yield break;

        try
        {
            if (TryGetAnimation(yawn, out var animator, out var id, out var type)
                && type == AnimatorControllerParameterType.Bool)
                animator.SetBool(id, false);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: ending the yawn animation failed: {exception.Message}");
        }
    }

    private static void ResetAnimation(InitiateYawn yawn)
    {
        if (!TryGetAnimation(yawn, out var animator, out var id, out var type))
            return;

        if (type == AnimatorControllerParameterType.Trigger)
            animator.ResetTrigger(id);
        else if (type == AnimatorControllerParameterType.Bool)
            animator.SetBool(id, false);
    }

    private static bool TryGetAnimation(InitiateYawn yawn, out Animator animator, out int id, out AnimatorControllerParameterType type)
    {
        animator = yawn._animator;
        id = yawn._animYawnId;
        type = default;

        if (!animator)
            return false;

        if (ParameterTypes.TryGetValue(id, out type))
            return true;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.nameHash != id)
                continue;

            type = parameter.type;
            ParameterTypes[id] = type;
            SrLogger.LogMessage($"Sloomber yawn animator parameter is a {type}");
            return true;
        }

        return false;
    }
}