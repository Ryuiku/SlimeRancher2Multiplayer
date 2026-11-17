using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Sanctuary;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Sanctuary;

[PacketHandler((byte)PacketType.SprinkleCanister)]
internal sealed class SprinkleCanisterHandler : BasePacketHandler<SprinkleCanisterPacket>
{
    protected override bool Handle(SprinkleCanisterPacket packet, IPEndPoint? _)
    {
        if (!NetworkActorManager.DeferDuringSceneLoad(() => NetworkSprinkleCanisterManager.Apply(packet.State)))
            NetworkSprinkleCanisterManager.Apply(packet.State);

        return true;
    }
}
