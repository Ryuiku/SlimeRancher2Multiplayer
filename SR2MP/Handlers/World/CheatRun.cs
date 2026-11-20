using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.World;

// The host already checked its cheat setting before sending this. The parameter is not named "_",
// because "out _" below would then bind to it instead of being a discard.
[PacketHandler((byte)PacketType.CheatRun, HandlerType.Client)]
internal sealed class CheatRunHandler : BasePacketHandler<CheatRunPacket>
{
    protected override bool Handle(CheatRunPacket packet, IPEndPoint? serverEp)
    {
        var result = packet.Action switch
        {
            CheatRunPacket.CheatAction.GrowGardens => NetworkCheats.GrowRanchGardens(out _),
            _ => "unknown action",
        };

        SrLogger.LogMessage($"Cheat menu: our part of {packet.Action} for another player's press: {result}");
        return false;
    }
}
