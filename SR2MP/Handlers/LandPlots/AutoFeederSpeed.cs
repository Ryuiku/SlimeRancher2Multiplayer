using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.AutoFeederSpeed)]
internal sealed class AutoFeederSpeedHandler : BasePacketHandler<AutoFeederSpeedPacket>
{
    protected override bool Handle(AutoFeederSpeedPacket packet, IPEndPoint? _)
    {
        if (string.IsNullOrEmpty(packet.ID)
            || !GameState.landPlots.TryGetValue(packet.ID, out var model)
            || model == null)
            return true;

        // Recorded even when the corral is not loaded here, so it is right when this player gets back.
        model.feederCycleSpeed = packet.Speed;
        SrLogger.LogMessage($"Feeder {packet.ID} speed set to {packet.Speed} by another player");

        if (!model.gameObj)
            return true;

        var feeder = model.gameObj.GetComponentInChildren<SlimeFeeder>(true);

        // SetFeederSpeed throws when the feeder has no model yet; the speed is already in the plot.
        if (!feeder || feeder._model == null)
            return true;

        HandlingPacket = true;
        try
        {
            feeder.SetFeederSpeed(packet.Speed);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not set the speed of feeder {packet.ID}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}
