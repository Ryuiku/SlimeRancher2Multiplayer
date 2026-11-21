using System.Net;
using Il2CppMonomiPark.SlimeRancher.Event;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;

namespace SR2MP.Handlers.Map;

[PacketHandler((byte)PacketType.MapUnlock)]
internal sealed class MapUnlockHandler : BasePacketHandler<MapUnlockPacket>
{
    protected override bool Handle(MapUnlockPacket packet, IPEndPoint? _)
    {
        if (!string.IsNullOrEmpty(packet.EventKey))
        {
            try
            {
                // Through the game's own zone event when it is the zone key, so weather, radiant
                // slimes, the sanctuary and the pedia react here as they do for the player who
                // entered it.
                var added = packet.EventKey == SR2MP.Shared.Utils.MapEvents.ZoneEventKey
                    ? SR2MP.Shared.Utils.MapEvents.RaiseZone(packet.NodeID)
                    : SR2MP.Shared.Utils.MapEvents.Record(packet.EventKey, packet.NodeID);
                if (added)
                    SR2MP.Shared.Utils.MapEvents.Refresh();
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not record {packet.EventKey} {packet.NodeID}: {ex.Message}");
            }

            return true;
        }

        // Record the unlock first: the visuals below need the map objects to be loaded, and when
        // they threw, the unlock was neither saved here nor passed on to the other players.
        // A record that already exists is kept as it is: rewriting it reset its count and times.
        bool isNew;
        try
        {
            isNew = SR2MP.Shared.Utils.MapEvents.Record(MapEventKey, packet.NodeID);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not record map unlock {packet.NodeID}: {ex.Message}");
            return true;
        }

        if (!isNew)
            return true;

        try
        {
            var gameEvent = Resources.FindObjectsOfTypeAll<StaticGameEvent>().FirstOrDefault(x => x._dataKey == packet.NodeID);
            if (gameEvent != null)
                SceneContext.Instance.MapDirector.NotifyZoneUnlocked(gameEvent, false, 0);

            var activator = Resources.FindObjectsOfTypeAll<MapNodeActivator>()
                .FirstOrDefault(x => x._fogRevealEvent != null && x._fogRevealEvent._dataKey == packet.NodeID);
            if (activator != null && activator.gameObject.activeInHierarchy)
                activator.StartCoroutine(activator.ActivateHologramAnimation());
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Map unlock {packet.NodeID} recorded, visuals skipped: {ex.Message}");
        }

        // An open map shows the newly revealed area without being reopened.
        SR2MP.Shared.Utils.MapEvents.Refresh();

        return true;
    }
}