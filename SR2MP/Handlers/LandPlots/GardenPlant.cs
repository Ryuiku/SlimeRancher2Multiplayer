using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.GardenPlant)]
internal sealed class GardenPlantHandler : BasePacketHandler<GardenPlantPacket>
{
    protected override bool Handle(GardenPlantPacket packet, IPEndPoint? clientEp)
    {
        try
        {
            Apply(packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Garden {packet.ID}: could not apply a plant from another player: {ex}");
        }

        if (!IsServerSide)
            return true;

        // The host's plot is what every game ends up with, the sender's included. Two players
        // planting into the same plot at once each kept their own crop when the host only passed
        // each plant on to the others.
        Main.Server.SendToAll(packet);
        return false;
    }

    private static void Apply(GardenPlantPacket packet)
    {
        // A garden this save never loaded gets a model, so the crop is there when it loads.
        var model = NetworkLandPlotManager.GetOrCreateModel(packet.ID);
        if (model == null)
        {
            SrLogger.LogWarning($"Garden {packet.ID}: no plot here, plant ignored");
            return;
        }

        if (packet.ActorType == NetworkGardenPlanting.ClearCrop)
        {
            NetworkGardenPlanting.Clear(model, packet.ID);
            return;
        }

        if (!ActorManager.ActorTypes.TryGetValue(packet.ActorType, out var crop) || crop == null)
        {
            SrLogger.LogWarning($"Garden {packet.ID}: unknown crop type {packet.ActorType}, plant ignored");
            return;
        }

        NetworkGardenPlanting.Plant(model, crop, packet.ID);
    }
}
