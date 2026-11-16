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
        return true;
    }

    /// <summary>
    /// Records a pod's state and shows it. Does not run the pod's rewards: blueprints, upgrade
    /// components and spawned items are shared by their own packets, so running them here gave
    /// them out again on every machine. Works when the pod is not loaded here, and when this
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