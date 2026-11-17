using System.Net;
using SR2MP.Components.Drone;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Drone;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Drone;

[PacketHandler((byte)PacketType.DroneUpdate)]
internal sealed class DroneUpdateHandler : BasePacketHandler<DroneUpdatePacket>
{
    protected override bool Handle(DroneUpdatePacket packet, IPEndPoint? _)
    {
        if (NetworkDrone.Drones.TryGetValue(packet.StationId, out var drone) && drone)
        {
            drone.ApplyUpdate(packet);
            return true;
        }

        // Not loaded here: keep the models up to date instead of dropping the update.
        try
        {
            NetworkDroneManager.ApplyUpdateToModel(packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to write drone update to the model for station {packet.StationId}: {ex.Message}");
        }

        return true;
    }
}
