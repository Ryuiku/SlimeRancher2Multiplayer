using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Actor;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Actor;

[PacketHandler((byte)PacketType.ActorTransfer)]
internal sealed class ActorTransferHandler : BasePacketHandler<ActorTransferPacket>
{
    protected override bool Handle(ActorTransferPacket packet, IPEndPoint? _)
    {
        // False when we kept the actor because we claimed it at the same moment. Our own claim was
        // sent again in that case, so the other player's claim is not passed on.
        return NetworkActorManager.ApplyOwnership(packet);
    }
}