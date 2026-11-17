using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using SR2MP.Packets.World;
using SR2MP.Server.Managers;

namespace SR2MP.Client.Managers;

internal static class NetworkWeatherManager
{
    public static WeatherRegistry Registry => SceneContext.Instance.WeatherRegistry;

    private static bool IsSceneDirector(WeatherDirector? candidate)
        => candidate && candidate!.gameObject.scene.IsValid() && candidate.gameObject.activeInHierarchy;

    public static LightningStrike Lightning
    {
        get
        {
            if (!lightning)
            {
                lightning = Resources.FindObjectsOfTypeAll<LightningStrike>().First(x => x.BlastPower < 2749f);
            }

            return lightning;
        }
    }

    private static LightningStrike lightning;

    public static readonly Dictionary<int, WeatherStateDefinition> WeatherStates = new();

    private static void Initialize()
    {
        var refer = GameContext.Instance.AutoSaveDirector._saveReferenceTranslation;
        foreach (var state in refer._weatherStateTranslation.RawLookupDictionary)
        {
            WeatherStates.Add(refer.GetPersistenceId(state.value), state.value.TryCast<WeatherStateDefinition>()!);
        }
    }

    public static void CheckInitialized()
    {
        if (WeatherStates.Count == 0)
            Initialize();
    }

    public static int GetPersistentID(WeatherStateDefinition state)
        => GameContext.Instance.AutoSaveDirector._saveReferenceTranslation
            .GetPersistenceId(state.Cast<IWeatherState>());

    // The first few problems go to the normal log so a tester's log shows them, the rest only to debug.
    private static int loggedWeatherProblems;

    private static void LogWeatherProblem(string message)
    {
        if (loggedWeatherProblems < 5)
        {
            loggedWeatherProblems++;
            SrLogger.LogWarning(message);
        }
        else
        {
            SrLogger.LogDebug(message);
        }
    }

    // The states the host has running in each zone (by zone name), from the last packet. Kept so
    // a zone that loads between packets gets its weather at once.
    private static readonly Dictionary<string, List<WeatherStateDefinition>> RunningByZone = new();

    private static int lastMapSignature;

    /// <summary>
    /// Client: takes the host's forecast as it is. Each zone's forecast is rebuilt in full (upcoming
    /// entries too, so the map shows them) and its forecast-unlocked flag set. The registry update
    /// stays blocked here, so nothing upcoming is started locally; only states that changed are
    /// started or stopped, on the zone's own director, so weather that is already running is left alone.
    /// </summary>
    internal static void Apply(WeatherPacket packet, bool immediate)
    {
        if (Main.Server.IsRunning)
            return;

        var registry = SceneContext.Instance ? SceneContext.Instance.WeatherRegistry : null;
        if (!registry || registry!._zones == null)
        {
            SrLogger.LogDebug("NetworkWeatherManager.Apply: WeatherRegistry not ready, dropping packet");
            return;
        }

        WeatherUpdateHelper.EnsureLookupInitialized();
        HandlingPacket = true;

        try
        {
            var zones = new List<(ZoneDefinition Key, WeatherRegistry.ZoneWeatherData Data)>();
            foreach (var pair in registry._zones)
            {
                if (pair.Key && !string.IsNullOrEmpty(pair.Key.name) && pair.Value != null)
                    zones.Add((pair.Key, pair.Value));
            }

            var signature = new HashCode();

            foreach (var (zoneKey, zone) in zones)
            {
                packet.Zones.TryGetValue(zoneKey.name, out var data);

                try
                {
                    RebuildForecast(zoneKey, zone, data, ref signature);
                }
                catch (Exception ex)
                {
                    LogWeatherProblem($"Weather: could not apply the forecast of {zoneKey.name}: {ex.Message}");
                }

                var running = new List<WeatherStateDefinition>();
                if (data != null)
                {
                    foreach (var forecast in data.Forecasts)
                    {
                        if (forecast.Started && forecast.State && !running.Any(s => s.Pointer == forecast.State!.Pointer))
                            running.Add(forecast.State!);
                    }
                }

                RunningByZone[zoneKey.name] = running;
                SyncRunningStates(zoneKey, zone, running, immediate);
            }

            var mapSignature = signature.ToHashCode();
            if (mapSignature != lastMapSignature)
            {
                lastMapSignature = mapSignature;
                RefreshMapWeather();
            }
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    private static void RebuildForecast(
        ZoneDefinition zoneKey,
        WeatherRegistry.ZoneWeatherData zone,
        WeatherZoneData? data,
        ref HashCode signature)
    {
        // Built before the old one is touched, so one bad entry can't leave the zone with nothing.
        var newEntries = new List<WeatherModel.ForecastEntry>();

        if (data != null)
        {
            foreach (var forecast in data.Forecasts)
            {
                if (!forecast.State)
                    continue;

                var pattern = WeatherUpdateHelper.ResolvePattern(zoneKey, zone, forecast.PatternName, forecast.State!);

                // The map reads the entry's pattern (its icon); an entry without one would break it.
                // Its state still runs if the host has it running (see Apply).
                if (!pattern)
                    continue;

                newEntries.Add(new WeatherModel.ForecastEntry
                {
                    State = forecast.State!.Cast<IWeatherState>(),
                    Pattern = pattern,
                    Started = forecast.Started,
                    StartTime = forecast.StartTime,
                    EndTime = forecast.EndTime,
                    IsEndTimeKnown = forecast.IsEndTimeKnown
                });

                signature.Add(pattern!.Pointer);
                signature.Add(forecast.State.Pointer);
                signature.Add(forecast.Started);
                signature.Add(forecast.StartTime);
                signature.Add(forecast.EndTime);
            }
        }

        // Cleared in place: the game holds on to this list.
        var list = zone.Forecast;
        if (list != null)
        {
            list.Clear();
            foreach (var entry in newEntries)
                list.Add(entry);
        }

        var unlocked = data?.ForecastUnlocked ?? false;
        zone.ForecastUnlocked = unlocked;
        signature.Add(zoneKey.name);
        signature.Add(unlocked);

        if (data != null && zone.Parameters != null)
            zone.Parameters.WindDirection = data.WindDirection;
    }

    /// <summary>
    /// Puts the zone's own director (if that zone is loaded here) on exactly the states the host has
    /// running: stops what the host no longer runs, starts what it newly runs, leaves the rest alone.
    /// </summary>
    private static void SyncRunningStates(
        ZoneDefinition zoneKey,
        WeatherRegistry.ZoneWeatherData zone,
        List<WeatherStateDefinition> wanted,
        bool immediate)
    {
        WeatherDirector? zoneDirector;
        try
        {
            zoneDirector = zone.Director;
        }
        catch
        {
            return;
        }

        if (!IsSceneDirector(zoneDirector))
            return;

        var director = zoneDirector!;
        var parameters = zone.Parameters;

        var current = new List<IWeatherState>();
        try
        {
            var runningStates = director._runningStates;
            if (runningStates != null)
            {
                foreach (var state in runningStates)
                {
                    if (state != null)
                        current.Add(state);
                }
            }
        }
        catch (Exception ex)
        {
            LogWeatherProblem($"Weather: could not read the running states of {zoneKey.name}: {ex.Message}");
            return;
        }

        foreach (var state in current)
        {
            if (wanted.Any(w => w.Pointer == state.Pointer))
                continue;

            try
            {
                director.StopState(state, parameters, immediate);
            }
            catch (Exception ex)
            {
                LogWeatherProblem($"Weather: could not stop a state in {zoneKey.name}: {ex.Message}");
            }
        }

        foreach (var state in wanted)
        {
            if (current.Any(c => c.Pointer == state.Pointer))
                continue;

            try
            {
                director.RunState(state.Cast<IWeatherState>(), parameters, immediate);
            }
            catch (Exception ex)
            {
                LogWeatherProblem($"Weather: could not start {state.name} in {zoneKey.name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Client: a zone's weather director just registered (the zone loaded). The game would start the
    /// registry's own running patterns on it, which are not the host's here, so start the host's.
    /// </summary>
    internal static void OnZoneDirectorRegistered(ZoneDefinition zoneKey)
    {
        if (Main.Server.IsRunning || !zoneKey)
            return;

        var registry = SceneContext.Instance ? SceneContext.Instance.WeatherRegistry : null;
        if (!registry || registry!._zones == null)
            return;

        if (!registry._zones.TryGetValue(zoneKey, out var zone) || zone == null)
            return;

        if (!RunningByZone.TryGetValue(zoneKey.name, out var wanted))
            return;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            SyncRunningStates(zoneKey, zone, wanted, true);
        }
        finally
        {
            HandlingPacket = wasHandling;
        }
    }

    internal static void ResetSession()
    {
        RunningByZone.Clear();
        lastMapSignature = 0;
    }

    // Redraws the weather icons of an open map; a map opened later draws them itself.
    private static void RefreshMapWeather()
    {
        try
        {
            var zoomedOutUI = ActiveMapUI ? ActiveMapUI!._zoomedOutUI : null;
            if (zoomedOutUI == null || zoomedOutUI._zoneMarkerUIs == null)
                return;

            foreach (var markerUI in zoomedOutUI._zoneMarkerUIs)
            {
                var zoneMarkerUI = markerUI != null ? markerUI.TryCast<ZoneMarkerUI>() : null;
                if (zoneMarkerUI)
                    zoneMarkerUI!.SetUpWeather();
            }
        }
        catch (Exception ex)
        {
            LogWeatherProblem($"Weather: could not refresh the map: {ex.Message}");
        }
    }
}