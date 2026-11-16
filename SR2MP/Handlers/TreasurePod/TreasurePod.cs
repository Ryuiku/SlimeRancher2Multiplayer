using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.TreasurePod;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.TreasurePod;

[PacketHandler((byte)PacketType.TreasurePod)]
internal sealed class TreasurePodHandler : BasePacketHandler<TreasurePodPacket>
{
    protected override bool Handle(TreasurePodPacket packet, IPEndPoint? _)
    {
        ApplyState("pod" + packet.ID, Il2Cpp.TreasurePod.State.OPEN, false);
        UnlockAppearance(packet);
        return true;
    }

    private static void UnlockAppearance(TreasurePodPacket packet)
    {
        if (packet.AppearanceSlime < 0)
            return;

        try
        {
            if (!ActorManager.ActorTypes.TryGetValue(packet.AppearanceSlime, out var type) || !type)
                return;

            var slime = type.TryCast<SlimeDefinition>();
            var director = SceneContext.Instance.SlimeAppearanceDirector;
            if (!slime || !director)
                return;

            var appearance = director.GetSpecificSlimeAppearance(type, packet.AppearanceSet);
            if (!appearance || director.IsAppearanceUnlocked(slime, appearance))
                return;

            HandlingPacket = true;
            director.UnlockAppearance(slime, appearance);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not unlock the appearance from pod {packet.ID}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    /// <summary>
    /// Records a pod's state and shows it. Does not run the pod's rewards: blueprints, upgrade
    /// components and spawned items are shared by their own packets (and a slime appearance comes
    /// with the pod packet), so running them here gave them out again on every machine. Works when the pod is not loaded here, and when this
    /// save has never seen the pod at all.
    /// </summary>
    internal static void ApplyState(string podKey, Il2Cpp.TreasurePod.State state, bool immediate)
    {
        try
        {
            if (!GameState.pods.TryGetValue(podKey, out var model) || model == null)
                model = GameState.InitializeTreasurePodModel(podKey);

            if (model == null)
                return;

            if (model.state == null)
                model.state = new ObservableValue<Il2Cpp.TreasurePod.State>(state);
            else if (model.state.Value != state)
                model.state.Set(state);

            if (!model.gameObj)
                return;

            var pod = model.gameObj.GetComponent<Il2Cpp.TreasurePod>();
            if (!pod)
                return;

            HandlingPacket = true;
            pod._forceUpdate = true;
            if (immediate)
                pod.UpdateImmediate(state);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply treasure pod state for {podKey}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }
    }
}