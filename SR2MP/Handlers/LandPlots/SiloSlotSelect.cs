using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.SiloSlotSelect)]
internal sealed class SiloSlotSelectHandler : BasePacketHandler<SiloSlotSelectPacket>
{
    protected override bool Handle(SiloSlotSelectPacket packet, IPEndPoint? _)
    {
        var model = NetworkLandPlotManager.GetOrCreateModel(packet.ID);
        if (model == null)
            return true;

        var indices = model.siloStorageIndices;
        if (indices != null && packet.Side < indices.Length)
            indices[packet.Side] = packet.Index;

        if (!model.gameObj) return true;

        model.gameObj.GetComponentsInChildren<SiloStorageActivator>()
            .FirstOrDefault(activator => activator.ActivatorIdx == packet.Side)?
            .OnActiveSlotChanged();

        return true;
    }
}