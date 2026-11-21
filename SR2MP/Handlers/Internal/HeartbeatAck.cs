using System.Net;
using SR2MP.Packets;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Internal;

// The host's heartbeat. Receiving it is all that matters (the client notes when it last heard
// from the host on its receive thread), so there is nothing to do here.
[PacketHandler((byte)PacketType.HeartbeatAck, HandlerType.Client)]
internal sealed class HeartbeatAckHandler : BasePacketHandler<EmptyPacket>
{
    protected override bool Handle(EmptyPacket packet, IPEndPoint? clientEp) => false;
}
