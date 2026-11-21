using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;

namespace SR2MP.Handlers.Time;

[PacketHandler((byte)PacketType.FastForward)]
internal sealed class BaseFastForwardHandler : BasePacketHandler<FastForwardPacket>
{
    // Game seconds. One game day is 86400.
    // The game's longest own skip is just under 36 game hours, and clocks can be a little apart.
    private const double MaxSkipAhead = 86400 * 3;

    protected override bool Handle(FastForwardPacket packet, IPEndPoint? _)
    {
        var timeDirector = SceneContext.Instance.TimeDirector;
        var now = timeDirector._worldModel.worldTime;

        if (packet.Time <= now)
            return true;

        // A skip far beyond a night's sleep means the sender's clock is not ours. Following it
        // would age the whole world by days.
        if (packet.Time - now > MaxSkipAhead)
        {
            SR2MP.Shared.Utils.FreezeLog.Record($"Ignored time skip from another player: {now:F0} -> {packet.Time:F0} ({(packet.Time - now) / 3600:F1} h)");
            SrLogger.LogWarning($"Ignored a time skip of {(packet.Time - now) / 3600:F1} game hours from another player.");
            return false;
        }

        // A skip that is already running to the same or a later time stays as it is. The game holds
        // a sleeping or knocked-out player in place until its own skip reaches the wake-up time, so
        // a shorter skip from another player must not replace it (on any machine, or clocks split).
        if (timeDirector.IsFastForwarding() && SR2MP.Patches.Time.OnFastForward.RunningTarget >= packet.Time)
        {
            SR2MP.Shared.Utils.FreezeLog.Record($"Kept running skip; time skip from another player: {now:F0} -> {packet.Time:F0} ({(packet.Time - now) / 3600:F1} h)");
            return true;
        }

        SR2MP.Shared.Utils.FreezeLog.Record($"Applied time skip from another player: {now:F0} -> {packet.Time:F0} ({(packet.Time - now) / 3600:F1} h)", important: true);

        HandlingPacket = true;
        try
        {
            timeDirector.FastForwardTo(packet.Time);
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}