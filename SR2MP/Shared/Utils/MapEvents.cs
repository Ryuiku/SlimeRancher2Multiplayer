using Il2CppMonomiPark.SlimeRancher.Event;
using Il2CppMonomiPark.SlimeRancher.World;

namespace SR2MP.Shared.Utils;

/// <summary>
/// The map shows an area once two records exist in the event table: its fog was revealed
/// ("fogRevealed") and its zone was unlocked (the map's zone event key). Both are shared.
/// </summary>
internal static class MapEvents
{
    private const string FallbackZoneEventKey = "zoneUnlocked";

    private static string? zoneEventKey;

    // Zone definitions by the data key their unlock record uses, built once per session.
    private static Dictionary<string, ZoneDefinition>? zonesByKey;

    static MapEvents()
    {
        Main.Server.OnServerStarted +=      ClearCache;
        Main.Client.OnConnected     += _ => ClearCache();
        Main.Client.OnDisconnected  +=      ClearCache;
    }

    private static void ClearCache()
    {
        zoneEventKey = null;
        zonesByKey = null;
    }

    internal static string ZoneEventKey
    {
        get
        {
            if (zoneEventKey != null)
                return zoneEventKey;

            // This runs for every game event (through the event table patch), so the fallback is
            // kept as well once the scene can be asked; otherwise every vac or shot would pay for
            // the lookup and the exception again.
            try
            {
                var context = SceneContext.Instance;
                if (!context || !context.MapDirector)
                    return FallbackZoneEventKey;

                var key = context.MapDirector._zoneEventProducer?.KeyPrefix;
                return zoneEventKey = string.IsNullOrEmpty(key) ? FallbackZoneEventKey : key!;
            }
            catch
            {
                return zoneEventKey = FallbackZoneEventKey;
            }
        }
    }

    /// <summary>
    /// Unlocks a zone the way the game does when a player first enters it, so its own reactions
    /// run here too (weather, radiant slimes, sanctuary, pedia). Writing only the record showed
    /// the zone on the map, but those caught up only after a reload. Falls back to the record.
    /// Returns true when the zone was new here.
    /// </summary>
    internal static bool RaiseZone(string dataKey)
    {
        try
        {
            var producer = SceneContext.Instance.MapDirector._zoneEventProducer;
            if (producer && TryFindZone(producer!, dataKey, out var zone))
            {
                if (producer!.HasRecordedEntryForData(zone))
                    return false;

                var wasHandling = HandlingPacket;
                HandlingPacket = true;
                try
                {
                    producer.RaiseEventForData(zone);
                }
                finally
                {
                    HandlingPacket = wasHandling;
                }

                return true;
            }

            SrLogger.LogDebug($"Zone {dataKey} has no zone definition here; recording it directly");
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not raise zone unlock {dataKey}, recording it directly: {ex.Message}");
        }

        return Record(ZoneEventKey, dataKey);
    }

    /// <summary>
    /// Unlocks zones whose map areas were revealed but whose zone unlock is missing. The game
    /// only unlocks a zone for the player who walks into it, and older versions of the mod never
    /// passed that on, so a host save can have Starlight Strand's map areas ("strand1".."strand3")
    /// revealed and still show the zone as unknown. An area's key is its zone's key followed by a
    /// number; areas named otherwise (such as the Labyrinth's) are left alone, and only zones the
    /// game knows are unlocked. Returns the keys of the zones it unlocked.
    /// </summary>
    internal static List<string> RepairZonesFromRevealedAreas()
    {
        var repaired = new List<string>();
        try
        {
            var context = SceneContext.Instance;
            if (!context || !context.MapDirector || context.eventDirector?._model == null)
                return repaired;

            var producer = context.MapDirector._zoneEventProducer;
            if (!producer)
                return repaired;

            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var area in Keys(MapEventKey))
            {
                if (string.IsNullOrEmpty(area))
                    continue;

                var end = area!.Length;
                while (end > 0 && char.IsDigit(area[end - 1]))
                    end--;

                // Only "name" followed by a number: "waterworks" or "strandToLabyrinth" don't
                // say which zone they are in.
                if (end == area.Length || end == 0)
                    continue;

                candidates.Add(area.Substring(0, end));
            }

            foreach (var zoneKey in candidates)
            {
                if (!TryFindZone(producer!, zoneKey, out var zone))
                    continue;

                if (producer!.HasRecordedEntryForData(zone))
                    continue;

                if (RaiseZone(zoneKey))
                {
                    repaired.Add(zoneKey);
                    SrLogger.LogMessage($"Map: unlocked zone {zoneKey}, its map areas were revealed but the zone was not.");
                }
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not unlock zones from revealed map areas: {ex.Message}");
        }

        if (repaired.Count > 0)
            Refresh();

        return repaired;
    }

    private static bool TryFindZone(ZoneEventProducer producer, string dataKey, out ZoneDefinition zone)
    {
        // A zone missing from the cache may only have been loaded since it was built; zone
        // unlocks are rare, so it is rebuilt once for it.
        if (zonesByKey != null && !zonesByKey.ContainsKey(dataKey))
            zonesByKey = null;

        if (zonesByKey == null || zonesByKey.Count == 0)
        {
            var map = new Dictionary<string, ZoneDefinition>();
            foreach (var definition in Resources.FindObjectsOfTypeAll<ZoneDefinition>())
            {
                if (!definition)
                    continue;

                try
                {
                    var key = producer.GetKeySuffixForData(definition);
                    if (!string.IsNullOrEmpty(key))
                        map[key] = definition;
                }
                catch
                {
                    // a zone the producer can't name is never unlocked through it
                }
            }

            zonesByKey = map;
        }

        if (zonesByKey.TryGetValue(dataKey, out var found) && found)
        {
            zone = found;
            return true;
        }

        zone = null!;
        return false;
    }

    internal static List<string?> Keys(string eventKey)
    {
        var list = new List<string?>();
        var table = SceneContext.Instance.eventDirector._model.table;
        if (table.TryGetValue(eventKey, out var entries))
        {
            foreach (var entry in entries)
                list.Add(entry.Key);
        }

        return list;
    }

    /// <summary>Adds the record if it is missing. Returns true when it was added.</summary>
    internal static bool Record(string eventKey, string dataKey)
    {
        var table = SceneContext.Instance.eventDirector._model.table;
        if (!table.TryGetValue(eventKey, out var entries))
        {
            table[eventKey] = new CppCollections.Dictionary<string, EventRecordModel.Entry>();
            entries = table[eventKey];
        }

        if (entries.ContainsKey(dataKey))
            return false;

        entries[dataKey] = new EventRecordModel.Entry
        {
            count = 1,
            createdRealTime = 0,
            createdGameTime = 0,
            dataKey = dataKey,
            eventKey = eventKey,
            updatedRealTime = 0,
            updatedGameTime = 0
        };
        return true;
    }

    internal static void Refresh()
    {
        try
        {
            SceneContext.Instance.eventDirector._model.NotifyParticipants();
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Map refresh: notify failed: {ex.Message}");
        }

        try
        {
            foreach (var activator in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.UI.Map.MapNodeActivator>())
            {
                if (activator && activator.gameObject.activeInHierarchy)
                    activator.UpdateHologramState();
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Map refresh: holograms failed: {ex.Message}");
        }
    }
}
