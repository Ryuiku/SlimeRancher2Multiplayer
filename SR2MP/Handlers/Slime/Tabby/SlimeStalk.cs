using System.Net;
using Il2CppMonomiPark.SlimeRancher.Slime;
using SR2MP.Components.Actor;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Slime.Tabby;
using SR2MP.Packets.Utils;
using SR2MP.Patches.Slime.Tabby;

namespace SR2MP.Handlers.Slime.Tabby;

// Shows another player's tabby stalking: the crouch and butt-wiggle before it pounces, and its
// stealth. The copy here is frozen and its own stalking does not run, so this only changes what the
// tabby looks like. It cannot eat what it pounces on (only the owner's slime eats).
[PacketHandler((byte)PacketType.SlimeStalk)]
internal sealed class SlimeStalkHandler : BasePacketHandler<SlimeStalkPacket>
{
    // Whether the slime animator's butt-wiggle parameter is a bool or a trigger, by its hash.
    private static readonly Dictionary<int, AnimatorControllerParameterType> ParameterTypes = new();

    protected override bool Handle(SlimeStalkPacket packet, IPEndPoint? _)
    {
        if (!ActorManager.Actors.TryGetValue(packet.ActorId.Value, out var model))
            return true;

        if (!model.TryGetNetworkComponent(out var networkComponent))
            return true;

        // A late packet from the previous owner: this game drives the tabby now.
        if (networkComponent.LocallyOwned)
            return true;

        var stalk = networkComponent.GetComponent<StalkConsumable>();
        if (!stalk)
            return true;

        StalkConsumableSync.OnReceived(packet.ActorId.Value, packet.Stealth);

        var mode = (StalkConsumable.Mode)packet.Mode;

        HandlingPacket = true;
        try
        {
            try
            {
                var target = ResolveTarget(packet.TargetActorId);
                if (target)
                    stalk.SetTarget(target);

                // A step that acts on a target is skipped when the target is unknown here (not
                // loaded, or not a synced actor), as the game would use it. The animation below
                // still shows the crouch.
                if (target || mode == StalkConsumable.Mode.NONE)
                    stalk.SetMode(mode);
            }
            catch (Exception exception)
            {
                SrLogger.LogDebug($"SlimeStalk: setting the stalk step failed: {exception.Message}");
            }

            try
            {
                stalk.SetStealth(packet.Stealth);
            }
            catch (Exception exception)
            {
                SrLogger.LogDebug($"SlimeStalk: SetStealth failed, setting the slime's stealth directly: {exception.Message}");
                try
                {
                    var slimeStealth = stalk._stealth;
                    if (slimeStealth)
                        slimeStealth.SetStealth(packet.Stealth);
                }
                catch (Exception inner)
                {
                    SrLogger.LogDebug($"SlimeStalk: setting stealth failed: {inner.Message}");
                }
            }

            try
            {
                ShowWiggle(stalk, mode);
            }
            catch (Exception exception)
            {
                SrLogger.LogDebug($"SlimeStalk: wiggle animation failed: {exception.Message}");
            }
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }

    private static GameObject? ResolveTarget(long actorId)
    {
        if (actorId == 0)
            return null;

        if (!ActorManager.Actors.TryGetValue(actorId, out var targetModel))
            return null;

        return targetModel.TryGetNetworkComponent(out var targetActor) ? targetActor.gameObject : null;
    }

    // The wiggle may be started from the stalking code, which does not run on this copy, so it is
    // also set directly. Doing it twice is harmless.
    private static void ShowWiggle(StalkConsumable stalk, StalkConsumable.Mode mode)
    {
        var applicator = stalk.GetComponent<SlimeAppearanceApplicator>();
        if (!applicator)
            return;

        var animator = applicator._animator;
        if (!animator)
            return;

        var id = stalk._animButtWiggleId;
        if (!TryGetParameterType(animator, id, out var type))
            return;

        var crouching = mode == StalkConsumable.Mode.PREP;

        if (type == AnimatorControllerParameterType.Bool)
            animator.SetBool(id, crouching);
        else if (type == AnimatorControllerParameterType.Trigger && crouching)
            animator.SetTrigger(id);
    }

    private static bool TryGetParameterType(Animator animator, int id, out AnimatorControllerParameterType type)
    {
        if (ParameterTypes.TryGetValue(id, out type))
            return true;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.nameHash != id)
                continue;

            type = parameter.type;
            ParameterTypes[id] = type;
            SrLogger.LogDebug($"Tabby butt-wiggle animator parameter is a {type}");
            return true;
        }

        return false;
    }
}
