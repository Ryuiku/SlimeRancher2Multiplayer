using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.TreasurePod;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.TreasurePod;

[PacketHandler((byte)PacketType.InitialTreasurePods)]
internal sealed class InitialTreasurePodsHandler : BasePacketHandler<InitialTreasurePodsPacket>
{
    protected override bool Handle(InitialTreasurePodsPacket packet, IPEndPoint? _)
    {
        foreach (var (podId, podState) in packet.TreasurePods)
            TreasurePodHandler.ApplyState("pod" + podId, podState, true);

        return true;
    }
}