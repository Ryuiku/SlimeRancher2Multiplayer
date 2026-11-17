using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Labyrinth;

// Not passed on: the host runs the unlock and sends everyone its new forecast.
[PacketHandler((byte)PacketType.PrismaGroupUnlock, HandlerType.Server)]
internal sealed class PrismaGroupUnlockHandler : BasePacketHandler<PrismaGroupUnlockPacket>
{
    protected override bool Handle(PrismaGroupUnlockPacket packet, IPEndPoint? _)
    {
        if (!string.IsNullOrEmpty(packet.GroupName))
            NetworkPrismaManager.HandleGroupUnlock(packet.GroupName);

        return false;
    }
}
