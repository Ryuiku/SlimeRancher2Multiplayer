using System.Net;
using Il2CppMonomiPark.SlimeRancher.Event;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Map;

[PacketHandler((byte)PacketType.InitialMapEntries, HandlerType.Client)]
internal sealed class InitialMapLoadHandler : BasePacketHandler<InitialMapPacket>
{
    protected override bool Handle(InitialMapPacket packet, IPEndPoint? _)
    {
        // Added to what this save already has: the host's reveals and zone unlocks, and the
        // joiner keeps its own. The table used to be replaced and nothing was refreshed, so the
        // map kept showing only what the joiner had explored.
        var added = 0;
        try
        {
            foreach (var node in packet.UnlockedNodes)
            {
                if (!string.IsNullOrEmpty(node) && SR2MP.Shared.Utils.MapEvents.Record(MapEventKey, node))
                    added++;
            }

            // Zones go through the game's own unlock, so their weather, radiant slimes,
            // sanctuary and pedia reactions run here as well.
            foreach (var zone in packet.UnlockedZones)
            {
                if (!string.IsNullOrEmpty(zone) && SR2MP.Shared.Utils.MapEvents.RaiseZone(zone))
                    added++;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply the host's map: {ex.Message}");
        }

        SR2MP.Shared.Utils.MapEvents.Refresh();
        SrLogger.LogMessage($"Map: host sent {packet.UnlockedNodes.Count} revealed areas and {packet.UnlockedZones.Count} zones, {added} were new here.");
        return false;
    }
}
