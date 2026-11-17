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
        if (!GameState.landPlots.TryGetValue(packet.ID, out var model) || model == null)
            return true;

        // Written to the plot even when the corral is not loaded here, so this save (the host's
        // included) and a later join feed on the driver's schedule.
        try
        {
            model.nextFeedingTime = packet.NextFeedingTime;
            model.remainingFeedOperations = packet.RemainingFeedOperations;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not set the feeder schedule of {packet.ID}: {ex.Message}");
        }

        if (!model.gameObj)
            return true;

        var feeder = model.gameObj.GetComponentInChildren<SlimeFeeder>();
        if (!feeder)
            return true;

        feeder._nextEject = packet.NextTime;
        return true;
    }
}