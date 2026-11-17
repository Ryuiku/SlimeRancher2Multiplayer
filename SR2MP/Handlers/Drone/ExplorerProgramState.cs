using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Drone;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Drone;

[PacketHandler((byte)PacketType.ExplorerProgramState)]
internal sealed class ExplorerProgramStateHandler : BasePacketHandler<ExplorerProgramStatePacket>
{
    protected override bool Handle(ExplorerProgramStatePacket packet, IPEndPoint? _)
    {
        try
        {
            var stationModel = GameState.GetIdentifiableModel(new ActorId(packet.StationId))?.TryCast<DroneStationGadgetModel>();
            var program = stationModel?.Program;

            if (program == null)
            {
                // The station or its program is not set up here yet: written when it is.
                NetworkDroneManager.PendingProgramStates[packet.StationId] = packet;
                return true;
            }

            // This game runs the program itself (a handoff just happened): its own plan wins.
            if (NetworkDroneManager.IsProgramAuthority(packet.StationId))
                return true;

            NetworkDroneManager.ApplyProgramState(program, packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to apply explorer program state for station {packet.StationId}: {ex.Message}");
        }

        return true;
    }
}
