using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Sanctuary;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Sanctuary;

[PacketHandler((byte)PacketType.InitialSanctuary, HandlerType.Client)]
internal sealed class InitialSanctuaryHandler : BasePacketHandler<InitialSanctuaryPacket>
{
    protected override bool Handle(InitialSanctuaryPacket packet, IPEndPoint? _)
    {
        if (!NetworkActorManager.DeferDuringSceneLoad(() => NetworkSanctuaryManager.ApplyInitialPacket(packet)))
            NetworkSanctuaryManager.ApplyInitialPacket(packet);

        return false;
    }
}
