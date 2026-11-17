using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Labyrinth;
using SR2MP.Packets.World;

namespace SR2MP.Shared.Managers;

/// <summary>
/// The Labyrinth's prisma disruption forecast (which area is disrupted when) is rolled at random.
/// Every game rolled its own, so the same area could be calm for one player and disrupted for
/// another. The host's forecast is now the only one: clients never roll (Patches/Labyrinth/
/// PrismaDisruptionPatch.cs) and take the host's on join, on every change and every two minutes.
/// World time is already the host's, so the same forecast gives the same level everywhere.
/// </summary>
internal static class NetworkPrismaManager
{
    private const float HeartbeatSeconds = 120f;

    private static bool dirty;
    private static int lastSignature;
    private static float lastSent = -999f;

    private static readonly HashSet<string> WarnedUnknownAreas = new();

    internal static void MarkDirty() => dirty = true;

    private static PrismaDisruptionModel? GetModel()
    {
        try
        {
            return SceneContext.Instance && SceneContext.Instance.GameModel
                ? SceneContext.Instance.GameModel.GetPrismaDisruptionModel()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string AreaKey(DisruptionAreaDefinition definition)
    {
        string? guid = null;
        try
        {
            guid = definition.Guid;
        }
        catch
        {
            // fall back to the asset name
        }

        return string.IsNullOrEmpty(guid) ? definition.name : guid!;
    }

    internal static PrismaDisruptionPacket? CreatePacket()
    {
        var model = GetModel();
        return model == null ? null : CreatePacket(model);
    }

    private static PrismaDisruptionPacket CreatePacket(PrismaDisruptionModel model)
    {
        var packet = new PrismaDisruptionPacket { ForecastedTime = model._forecastedTime };

        foreach (var pair in model._areaDatas)
        {
            var definition = pair.key;
            var data = pair.value;
            if (!definition || data == null)
                continue;

            var area = new PrismaDisruptionPacket.Area
            {
                Guid = AreaKey(definition),
                LastCompletionTime = data.LastCompletionTime,
                ForecastUnlocked = data.ForecastUnlocked
            };

            if (data.Forecast != null)
            {
                foreach (var pattern in data.Forecast)
                {
                    if (pattern == null)
                        continue;

                    var wirePattern = new PrismaDisruptionPacket.Pattern { EndTime = pattern.EndTime };
                    if (pattern.Entries != null)
                    {
                        foreach (var entry in pattern.Entries)
                        {
                            if (entry != null)
                                wirePattern.Entries.Add(new PrismaDisruptionPacket.Entry { Start = entry.StartTime, Level = (byte)entry.Level });
                        }
                    }

                    area.Patterns.Add(wirePattern);
                }
            }

            packet.Areas.Add(area);
        }

        return packet;
    }

    // Cheap enough to work out every half second while something changed.
    private static int Signature(PrismaDisruptionModel model)
    {
        var hash = new HashCode();
        hash.Add(model._forecastedTime);

        foreach (var pair in model._areaDatas)
        {
            var data = pair.value;
            if (data == null)
                continue;

            hash.Add(data.LastCompletionTime);
            hash.Add(data.ForecastUnlocked);

            var forecast = data.Forecast;
            var count = forecast?.Count ?? 0;
            hash.Add(count);
            if (count == 0)
                continue;

            var first = forecast![0];
            var last = forecast[count - 1];
            if (first != null)
                hash.Add(first.StartTime);
            if (last != null)
                hash.Add(last.EndTime);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Host only, from the session loop: sends the forecast when it changed, and every two
    /// minutes in any case so a client that drifted (or missed one) is put right.
    /// </summary>
    internal static void HostTick()
    {
        if (!Main.Server.IsRunning || Main.Server.ClientManager.ClientCount == 0)
            return;

        var now = UnityEngine.Time.realtimeSinceStartup;
        var heartbeat = now - lastSent >= HeartbeatSeconds;
        if (!dirty && !heartbeat)
            return;

        dirty = false;

        var model = GetModel();
        if (model == null)
            return;

        var signature = Signature(model);
        if (!heartbeat && signature == lastSignature)
            return;

        lastSignature = signature;
        lastSent = now;
        Main.Server.SendToAll(CreatePacket(model));
    }

    private static Dictionary<string, DisruptionAreaDefinition> BuildDefinitionLookup(PrismaDisruptionModel model)
    {
        var lookup = new Dictionary<string, DisruptionAreaDefinition>();

        try
        {
            var config = model._disruptionWorldConfig;
            if (config && config.DisruptionAreaGroups != null)
            {
                foreach (var group in config.DisruptionAreaGroups)
                {
                    if (!group || group.DisruptionAreas == null)
                        continue;

                    foreach (var reference in group.DisruptionAreas)
                    {
                        var definition = reference.Definition;
                        if (definition)
                            lookup.TryAdd(AreaKey(definition), definition);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Prisma forecast: reading the area config failed: {ex.Message}");
        }

        foreach (var pair in model._areaDatas)
        {
            if (pair.key)
                lookup.TryAdd(AreaKey(pair.key), pair.key);
        }

        return lookup;
    }

    /// <summary>
    /// Client: replaces the local forecast with the host's, then puts every loaded area on the
    /// level that forecast gives for now. Written to the model, so areas not loaded here are
    /// right when they load.
    /// </summary>
    internal static void Apply(PrismaDisruptionPacket packet)
    {
        if (Main.Server.IsRunning)
            return;

        var model = GetModel();
        if (model == null)
        {
            SrLogger.LogWarning("Prisma forecast from host not applied: no disruption model here yet.");
            return;
        }

        var lookup = BuildDefinitionLookup(model);
        var now = SceneContext.Instance.TimeDirector._worldModel.worldTime;
        var listed = new HashSet<IntPtr>();
        var patterns = 0;

        HandlingPacket = true;
        try
        {
            foreach (var area in packet.Areas)
            {
                if (!lookup.TryGetValue(area.Guid, out var definition))
                {
                    if (WarnedUnknownAreas.Add(area.Guid))
                        SrLogger.LogWarning($"Prisma forecast from host names an area this game does not know: {area.Guid}");
                    continue;
                }

                listed.Add(definition.Pointer);

                PrismaDisruptionModel.DisruptionAreaData? data = null;
                if (!model._areaDatas.TryGetValue(definition, out data) || data == null)
                {
                    data = new PrismaDisruptionModel.DisruptionAreaData
                    {
                        Definition = definition,
                        Forecast = new Il2CppSystem.Collections.Generic.List<DisruptionPattern>()
                    };
                    model._areaDatas[definition] = data;
                }

                // Cleared in place: the game may hold on to this list.
                if (data.Forecast == null)
                    data.Forecast = new Il2CppSystem.Collections.Generic.List<DisruptionPattern>();
                data.Forecast.Clear();

                foreach (var pattern in area.Patterns)
                {
                    var entries = new Il2CppSystem.Collections.Generic.List<DisruptionPatternEntry>();
                    foreach (var entry in pattern.Entries)
                        entries.Add(new DisruptionPatternEntry(entry.Start, (DisruptionLevel)entry.Level));

                    data.Forecast.Add(new DisruptionPattern(entries, pattern.EndTime));
                    patterns++;
                }

                data.LastCompletionTime = area.LastCompletionTime;
                data.ForecastUnlocked = area.ForecastUnlocked;
                CacheChangeTime(data, now);
            }

            // Anything the host does not have is not forecast here either.
            foreach (var pair in model._areaDatas)
            {
                if (!pair.key || pair.value == null || listed.Contains(pair.key.Pointer))
                    continue;

                pair.value.Forecast?.Clear();
                pair.value.ForecastUnlocked = false;
                CacheChangeTime(pair.value, now);
            }

            model._forecastedTime = packet.ForecastedTime;

            var director = SceneContext.Instance.PrismaDirector;
            if (director)
                director._forecastedTime = packet.ForecastedTime;

            // Tells the map and other listeners the forecast changed. _areaRefreshNeeded is left
            // alone on purpose: a refresh may build new random patterns locally.
            try
            {
                model.NotifyParticipants();
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Prisma forecast: notifying listeners failed: {ex.Message}");
            }
        }
        finally
        {
            HandlingPacket = false;
        }

        SrLogger.LogMessage($"Prisma forecast from host applied: {listed.Count} areas, {patterns} patterns");

        // A level change can spawn or remove unstable resources, which must not happen mid-load.
        if (!NetworkActorManager.DeferDuringSceneLoad(PushCurrentLevels))
            PushCurrentLevels();
    }

    private static void CacheChangeTime(PrismaDisruptionModel.DisruptionAreaData data, double now)
    {
        try
        {
            data.CacheChangeTime(now);
        }
        catch
        {
            // Makes the game look at this area again on its next check.
            data.NextChangeTime = now;
        }
    }

    // Done the way the director's own update does it (not as a packet), so the level change
    // reaches the barriers, resources and effects of loaded areas right away.
    private static void PushCurrentLevels()
    {
        try
        {
            var director = SceneContext.Instance ? SceneContext.Instance.PrismaDirector : null;
            var model = GetModel();
            if (!director || model == null || director!._disruptionAreas == null)
                return;

            var now = SceneContext.Instance!.TimeDirector._worldModel.worldTime;
            var areas = new List<PrismaDirector.DisruptionArea>();
            foreach (var pair in director._disruptionAreas)
            {
                if (pair.value != null)
                    areas.Add(pair.value);
            }

            var changed = 0;
            foreach (var area in areas)
            {
                try
                {
                    var definition = area.Definition;
                    if (!definition)
                        continue;

                    var level = model.GetStateAtTime(definition, now);
                    if (level == area.DisruptionLevel)
                        continue;

                    director.SetDisruptionLevel(area, level, NetworkActorManager.InitialActorsLoaded);
                    changed++;
                }
                catch (Exception ex)
                {
                    SrLogger.LogWarning($"Could not set a Labyrinth area's disruption level: {ex.Message}");
                }
            }

            if (changed > 0)
                SrLogger.LogDebug($"Prisma forecast from host: {changed} loaded areas changed level");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply the host's disruption levels: {ex.Message}");
        }
    }

    /// <summary>
    /// Host: a client asked for a forecast group to be unlocked. Runs the game's own unlock
    /// unless the host already has it; the host patches then send the new forecast to everyone.
    /// </summary>
    internal static void HandleGroupUnlock(string groupName)
    {
        var director = SceneContext.Instance ? SceneContext.Instance.PrismaDirector : null;
        var model = GetModel();
        if (!director || model == null)
            return;

        var config = director!._disruptionWorldConfig;
        DisruptionAreaGroupConfig? group = null;
        if (config && config.DisruptionAreaGroups != null)
        {
            foreach (var candidate in config.DisruptionAreaGroups)
            {
                if (candidate && candidate.name == groupName)
                {
                    group = candidate;
                    break;
                }
            }
        }

        if (!group)
        {
            SrLogger.LogWarning($"A client unlocked disruption forecast group '{groupName}', which the host does not know.");
            return;
        }

        foreach (var reference in group!.DisruptionAreas)
        {
            var definition = reference.Definition;
            if (definition && model._areaDatas.TryGetValue(definition, out var data) && data != null && data.ForecastUnlocked)
            {
                SrLogger.LogDebug($"Disruption forecast group '{groupName}' from a client is already unlocked on the host.");
                MarkDirty();
                return;
            }
        }

        SrLogger.LogMessage($"A client unlocked disruption forecast group '{groupName}'; unlocking it on the host.");
        director.OnDisruptionGroupUnlocked(group);
        MarkDirty();
    }
}
