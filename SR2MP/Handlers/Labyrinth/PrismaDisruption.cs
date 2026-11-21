using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Labyrinth;

[PacketHandler((byte)PacketType.PrismaDisruption, HandlerType.Client)]
internal sealed class PrismaDisruptionHandler : BasePacketHandler<PrismaDisruptionPacket>
{
    protected override bool Handle(PrismaDisruptionPacket packet, IPEndPoint? _)
    {
        NetworkPrismaManager.Apply(packet);
        return false;
    }
}
