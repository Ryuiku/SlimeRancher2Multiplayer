using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Sanctuary;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Sanctuary;

[PacketHandler((byte)PacketType.SanctuaryRoom)]
internal sealed class SanctuaryRoomHandler : BasePacketHandler<SanctuaryRoomPacket>
{
    protected override bool Handle(SanctuaryRoomPacket packet, IPEndPoint? clientEp)
    {
        var serverSide = IsServerSide;

        // The host decides which gadget stands in each pre-placed spot, and writes the picks it
        // keeps at once. They only touch its world's data, and a pick that waited for the host's
        // own zone load lost the race against the gadget the host's room builds during that load.
        if (serverSide && packet.PreplacedGadgets.Count > 0)
        {
            NetworkSanctuaryManager.ArbitratePreplaced(packet, clientEp);
            ApplyPreplaced(packet, false);
        }

        // A theme swap rebuilds the room; never in the middle of a zone load. Passed on right away.
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet, !serverSide)))
            Apply(packet, !serverSide);

        // Nothing left to pass on when the host turned down every pick in it.
        return !string.IsNullOrEmpty(packet.ThemeRefId) || packet.PreplacedGadgets.Count > 0;
    }

    private static void Apply(SanctuaryRoomPacket packet, bool withPreplaced)
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

        if (withPreplaced)
            ApplyPreplaced(packet, true);
    }

    private static void ApplyPreplaced(SanctuaryRoomPacket packet, bool fromHostLive)
    {
        try
        {
            if (packet.PreplacedGadgets.Count > 0)
                NetworkSanctuaryManager.ApplyPreplaced(packet.RoomId, packet.PreplacedGadgets, fromHostLive);
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            SrLogger.LogWarning($"Could not apply a pre-placed gadget of sanctuary room {packet.RoomId}: {ex.Message}");
        }
    }
}
