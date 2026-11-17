using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Sanctuary;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Sanctuary;

[PacketHandler((byte)PacketType.InitialSprinkleCanisters, HandlerType.Client)]
internal sealed class InitialSprinkleCanistersHandler : BasePacketHandler<InitialSprinkleCanistersPacket>
{
    protected override bool Handle(InitialSprinkleCanistersPacket packet, IPEndPoint? _)
    {
        if (!NetworkActorManager.DeferDuringSceneLoad(() => NetworkSprinkleCanisterManager.ApplyInitialPacket(packet)))
            NetworkSprinkleCanisterManager.ApplyInitialPacket(packet);

        return false;
    }
}
