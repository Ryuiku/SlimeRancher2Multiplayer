using System.Collections;
using System.Net;
using Il2CppMonomiPark.SlimeRancher.Slime.Slumber;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Slime.Sloomber;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Slime.Sloomber;

// Shows another player's sloomber yawning: the wind-up (yawn animation, the cloud on its head
// fading, then the yawn face), the yawn itself, or the wind-up ending without a yawn. The copy here
// does not yawn on its own (only the owner's does), so this is all the others see. A cheat menu
// request to yawn is run by the sloomber's owner; the host passes it on when it is not the owner.
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

    // Sloombers whose yawn face was already shown during the current wind-up.
    private static readonly HashSet<long> FaceShown = new();

    protected override bool Handle(SloomberYawnPacket packet, IPEndPoint? clientEp)
    {
        // Runs with HandlingPacket off on purpose: the owner's yawn must be sent by its patches.
        if (packet.Request)
            return HandleRequest(packet, clientEp);

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

    // A cheat menu request: the owner yawns now through the game's own code, which sends the yawn.
    private static bool HandleRequest(SloomberYawnPacket packet, IPEndPoint? clientEp)
    {
        // The host's live setting decides for a client's request.
        if (Main.Server.IsRunning && clientEp != null && !Main.AllowCheats)
        {
            SrLogger.LogWarning($"Ignored a sloomber yawn request from {clientEp}: cheats are off on this server");
            return false;
        }

        if (ActorManager.Actors.TryGetValue(packet.ActorId.Value, out var model)
            && model.TryGetNetworkComponent(out var networkComponent)
            && networkComponent.LocallyOwned)
        {
            var yawn = networkComponent.GetComponent<InitiateYawn>();
            if (yawn)
                NetworkCheats.YawnNow(yawn);
            return false;
        }

        // Not this game's sloomber (or not loaded on the host): the host passes it on to its owner.
        return true;
    }

    private static void BeginWindUp(InitiateYawn yawn, long actorId)
    {
        ShowWindUp(yawn);

        WindUpShown[actorId] = UnityEngine.Time.time;
        FaceShown.Remove(actorId);
        var version = NextVersion(actorId);

        float faceDelay = 0f, waitTime = WindUpGrace;
        try
        {
            var metadata = yawn.InitiateYawnMetadata;
            if (metadata)
            {
                faceDelay = Mathf.Max(0f, metadata.YawnStartFaceTime);
                waitTime += faceDelay + Mathf.Max(0f, metadata.TimeUntilCloud);
            }
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: reading the yawn timings failed: {exception.Message}");
        }

        // The owner shows the yawn face YawnStartFaceTime after its wind-up starts.
        StartCoroutine(FaceLater(yawn, actorId, version, faceDelay));
        StartCoroutine(EndWindUpIfAbandoned(yawn, actorId, version, waitTime));
    }

    // The body animation and the cloud on its head, as the owner's InitiateYawn.Selected or nap yawn starts them.
    private static void ShowWindUp(InitiateYawn yawn)
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
    }

    private static void SetFace(InitiateYawn yawn, long actorId)
    {
        FaceShown.Add(actorId);
        try
        {
            var face = yawn._sfAnimator;
            if (face)
                face!.SetTrigger(FaceTriggers.YAWN);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: yawn face failed: {exception.Message}");
        }
    }

    private static IEnumerator FaceLater(InitiateYawn yawn, long actorId, int version, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (!IsCurrent(actorId, version) || !yawn)
            yield break;

        SetFace(yawn, actorId);
    }

    private static void CancelWindUp(InitiateYawn yawn, long actorId)
    {
        WindUpShown.Remove(actorId);
        FaceShown.Remove(actorId);
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
        var faceShown = FaceShown.Remove(actorId) && hadWindUp;
        var version = NextVersion(actorId);

        // No wind-up arrived (lost, or the yawn came too soon after it): show the animation and the
        // head cloud with the yawn, so it is not a bare puff.
        if (!hadWindUp)
            ShowWindUp(yawn);

        // The owner shows the face before the cloud; a wind-up whose face timer has not fired yet
        // (it was cut short by this yawn) still gets it.
        if (!faceShown)
            SetFace(yawn, actorId);
        FaceShown.Remove(actorId);

        try
        {
            yawn.DoYawn();
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: DoYawn failed: {exception.Message}");
        }

        float yawnTime = 1f, recoverTime = 1f;
        try
        {
            var metadata = yawn.InitiateYawnMetadata;
            if (metadata)
            {
                yawnTime = Mathf.Clamp(metadata.YawnTime, 0f, 10f);
                recoverTime = metadata.RecoverTime;
            }
        }
        catch
        {
            // Defaults.
        }

        // The owner ends the head cloud and particles YawnTime after the cloud, not at once.
        StartCoroutine(EndYawnLater(yawn, actorId, version, yawnTime));

        try
        {
            if (!TryGetAnimation(yawn, out var animator, out var id, out var type))
                return;

            if (type == AnimatorControllerParameterType.Trigger)
            {
                // A trigger left over from the wind-up must not play the animation a second time. One
                // set just now (no wind-up) must still play.
                if (hadWindUp)
                    animator.ResetTrigger(id);
            }
            else if (type == AnimatorControllerParameterType.Bool)
            {
                StartCoroutine(EndBoolLater(yawn, actorId, Mathf.Clamp(yawnTime + recoverTime, 0.5f, 10f)));
            }
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"SloomberYawn: ending the yawn animation failed: {exception.Message}");
        }
    }

    private static IEnumerator EndYawnLater(InitiateYawn yawn, long actorId, int version, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        // A new wind-up started meanwhile: leave its head cloud alone.
        if (!IsCurrent(actorId, version) || !yawn)
            yield break;

        EndReactables(yawn);
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