using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Drone;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Drone;

[PacketHandler((byte)PacketType.DroneExtractionQueue)]
internal sealed class DroneExtractionQueueHandler : BasePacketHandler<DroneExtractionQueuePacket>
{
    protected override bool Handle(DroneExtractionQueuePacket packet, IPEndPoint? _)
    {
        try
        {
            var stationModel = GameState.GetIdentifiableModel(new ActorId(packet.StationId))?.TryCast<DroneStationGadgetModel>();
            if (stationModel == null)
            {
                SrLogger.LogDebug($"DroneStationGadgetModel {packet.StationId} not found for its extraction queue");
                return true;
            }

            NetworkDroneManager.ApplyExtractionQueue(stationModel, packet.TypeIds);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to apply cloud extraction queue for station {packet.StationId}: {ex.Message}");
        }

        return true;
    }
}
