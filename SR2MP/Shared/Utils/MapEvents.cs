using Il2CppMonomiPark.SlimeRancher.Event;

namespace SR2MP.Shared.Utils;

/// <summary>
/// The map shows an area once two records exist in the event table: its fog was revealed
/// ("fogRevealed") and its zone was unlocked (the map's zone event key). Both are shared.
/// </summary>
internal static class MapEvents
{
    private static string? zoneEventKey;

    internal static string ZoneEventKey
    {
        get
        {
            if (zoneEventKey != null)
                return zoneEventKey;

            try
            {
                var key = SceneContext.Instance.MapDirector._zoneEventProducer?.KeyPrefix;
                if (!string.IsNullOrEmpty(key))
                    return zoneEventKey = key;
            }
            catch
            {
                // fall through
            }

            return "zoneUnlocked";
        }
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
