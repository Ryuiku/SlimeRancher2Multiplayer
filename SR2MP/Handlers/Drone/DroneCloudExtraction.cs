using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Drone;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Drone;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Drone;

[PacketHandler((byte)PacketType.DroneCloudExtraction)]
internal sealed class DroneCloudExtractionHandler : BasePacketHandler<DroneCloudExtractionPacket>
{
    protected override bool Handle(DroneCloudExtractionPacket packet, IPEndPoint? _)
    {
        var stationModel = GameState.GetIdentifiableModel(new ActorId(packet.StationId))?.TryCast<DroneStationGadgetModel>();
        if (stationModel == null)
        {
            SrLogger.LogWarning($"DroneStationGadgetModel {packet.StationId} not found!");
            return true;
        }

        // A station not loaded here needs nothing more: the items still to eject come as the
        // whole queue in DroneExtractionQueuePacket, which is written to the model either way.
        var stationObj = stationModel.GetGameObject();
        var station = stationObj ? stationObj!.GetComponentInChildren<DroneStation>() : null;
        if (!station)
            return true;

        var type = packet.TypeId != -1 && ActorManager.ActorTypes.TryGetValue(packet.TypeId, out var identType)
            ? identType
            : null;

        var queueBefore = NetworkDroneManager.GetExtractionQueueSize(stationModel);
        var cloud = GameState.droneModel.GetCloudModel();
        var cloudBefore = cloud != null && type != null ? cloud.GetAmount(type) : -1;

        HandlingPacket = true;
        try
        {
            station!.AssignCloudExtractionMode(packet.Extracting, type, packet.Count);
        }
        finally
        {
            HandlingPacket = false;
        }

        // For the in-game test: shows whether assigning also takes the items out of the cloud here
        // (the sender's cloud change already arrives as an absolute DroneCloudPacket).
        var cloudAfter = cloud != null && type != null ? cloud.GetAmount(type) : -1;
        SrLogger.LogDebug($"Drone station {packet.StationId}: extraction {(packet.Extracting ? "started" : "stopped")} for {packet.Count}; queue {queueBefore} -> {NetworkDroneManager.GetExtractionQueueSize(stationModel)}, cloud {cloudBefore} -> {cloudAfter}");

        return true;
    }
}
