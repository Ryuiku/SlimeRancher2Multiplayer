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
        TreasurePodHandler.RepairDuplicatePods();

        foreach (var (podKey, podState) in packet.TreasurePods)
        {
            if (!string.IsNullOrEmpty(podKey))
                TreasurePodHandler.ApplyState(podKey, podState, true);
        }

        return true;
    }
}