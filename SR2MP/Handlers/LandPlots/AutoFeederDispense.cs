using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.AutoFeederDispense)]
internal sealed class AutoFeederDispenseHandler : BasePacketHandler<AutoFeederDispensePacket>
{
    protected override bool Handle(AutoFeederDispensePacket packet, IPEndPoint? _)
    {
        if (string.IsNullOrEmpty(packet.ID)
            || !GameState.landPlots.TryGetValue(packet.ID, out var model)
            || model == null)
            return true;

        // Written to the plot even when the corral is not loaded here, so this save (the host's
        // included) and a later join feed on the schedule of the game that runs the feeder.
        // The schedule only moves forward: an older one (a handover overlap or a late packet) would
        // make this game count the same feedings again when it runs the feeder.
        try
        {
            var old = model.nextFeedingTime;
            if (packet.NextFeedingTime > old)
            {
                model.nextFeedingTime = packet.NextFeedingTime;
                model.remainingFeedOperations = Math.Max(0, packet.RemainingFeedOperations);
                if (old > 0d && packet.NextFeedingTime - old > 3600d * 6d)
                    SrLogger.LogMessage($"Feeder {packet.ID}: schedule moved forward {(packet.NextFeedingTime - old) / 3600d:F1} h by the player running it");
            }
            else if (packet.NextFeedingTime == old)
            {
                // Within one feeding cycle the queued feedings only go down.
                model.remainingFeedOperations = Math.Max(0, Math.Min(model.remainingFeedOperations, packet.RemainingFeedOperations));
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not set the feeder schedule of {packet.ID}: {ex.Message}");
        }

        try
        {
            if (!model.gameObj)
                return true;

            var feeder = model.gameObj.GetComponentInChildren<SlimeFeeder>(true);
            if (feeder)
                feeder._nextEject = UnityEngine.Time.timeAsDouble + Math.Max(0d, packet.EjectIn);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not set the next eject of feeder {packet.ID}: {ex.Message}");
        }

        return true;
    }
}
