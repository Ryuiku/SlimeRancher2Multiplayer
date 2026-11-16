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
        if (!GameState.landPlots.TryGetValue(packet.ID, out var model))
            return true;

        // Recorded even when the corral is not loaded here, so it is right when this player gets back.
        model.feederCycleSpeed = packet.Speed;

        if (!model.gameObj)
            return true;

        var feeder = model.gameObj.GetComponentInChildren<SlimeFeeder>();
        if (!feeder)
            return true;

        HandlingPacket = true;
        feeder.SetFeederSpeed(packet.Speed);
        HandlingPacket = false;

        return true;
    }
}