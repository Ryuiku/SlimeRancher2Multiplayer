using System.Net;
using SR2MP.Components.Actor;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.World;

[PacketHandler((byte)PacketType.BreakableBreak)]
internal sealed class BreakableBreakHandler : BasePacketHandler<BreakableBreakPacket>
{
    protected override bool Handle(BreakableBreakPacket packet, IPEndPoint? _)
    {
        try
        {
            if (!ActorManager.Actors.TryGetValue(packet.ActorId, out var model) || model == null)
                return true;

            var gameObject = model.GetGameObject();
            if (!gameObject)
                return true;

            var networkActor = gameObject.GetComponent<NetworkActor>();
            if (!networkActor || !networkActor.LocallyOwned)
                return true;

            var breakable = gameObject.GetComponent<BreakOnImpactBase>();
            if (!breakable || breakable._breaking)
                return true;

            // HandlingPacket stays off on purpose: the rewards and the crate's removal must be
            // announced by this game, the owner, exactly as if it had broken here on its own.
            NetworkBreakables.ApplyingRequest = true;
            try
            {
                breakable.BreakOpen();
            }
            finally
            {
                NetworkBreakables.ApplyingRequest = false;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not break crate {packet.ActorId} for another player: {ex.Message}");
        }

        return true;
    }
}
