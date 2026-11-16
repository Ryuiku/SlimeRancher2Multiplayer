using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;

namespace SR2MP.Handlers.Time;

[PacketHandler((byte)PacketType.FastForward)]
internal sealed class BaseFastForwardHandler : BasePacketHandler<FastForwardPacket>
{
    // Game seconds. One game day is 86400.
    private const double MaxSkipAhead = 86400 * 1.5;

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
            SrLogger.LogWarning($"Ignored a time skip of {(packet.Time - now) / 3600:F1} game hours from another player.");
            return false;
        }

        // The game holds a sleeping or knocked-out player in place until its own skip reaches the
        // wake-up time. Another player's shorter skip replaced that target: the skip stopped early
        // and this player stayed frozen while the rest of the time passed at normal speed.
        if (SR2MP.Components.UI.MultiplayerUI.IsLocalPlayerLocked(out var unlockTime) && packet.Time < unlockTime)
            return true;

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