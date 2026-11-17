using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Sanctuary;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Sanctuary;

[PacketHandler((byte)PacketType.SanctuaryRoom)]
internal sealed class SanctuaryRoomHandler : BasePacketHandler<SanctuaryRoomPacket>
{
    protected override bool Handle(SanctuaryRoomPacket packet, IPEndPoint? _)
    {
        // A theme swap rebuilds the room; never in the middle of a zone load. Passed on right away.
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
            Apply(packet);

        return true;
    }

    private static void Apply(SanctuaryRoomPacket packet)
    {
        try
        {
            if (!string.IsNullOrEmpty(packet.ThemeRefId))
                NetworkSanctuaryManager.ApplyTheme(packet.RoomId, packet.ThemeRefId);
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            SrLogger.LogWarning($"Could not apply the theme of sanctuary room {packet.RoomId}: {ex.Message}");
        }

        try
        {
            if (packet.PreplacedGadgets.Count > 0)
                NetworkSanctuaryManager.ApplyPreplaced(packet.RoomId, packet.PreplacedGadgets);
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            SrLogger.LogWarning($"Could not apply a pre-placed gadget of sanctuary room {packet.RoomId}: {ex.Message}");
        }
    }
}
