using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.InitialResourceSpawners, HandlerType.Client)]
internal sealed class InitialResourceSpawnersHandler : BasePacketHandler<InitialResourceSpawnersPacket>
{
    protected override bool Handle(InitialResourceSpawnersPacket packet, IPEndPoint? _)
    {
        NetworkGardenManager.ApplyInitialPacket(packet);
        return false;
    }
}
