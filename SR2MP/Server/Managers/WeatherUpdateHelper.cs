using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Server.Managers;

internal static class WeatherUpdateHelper
{
    private static bool lookupInitialized;

    // Every weather pattern the zone configs know, by asset name. The host sends each forecast
    // entry's pattern by name, so the client resolves exactly the host's pattern.
    private static readonly Dictionary<string, WeatherPatternDefinition> PatternsByName = new();

    // A pattern that could not be resolved is logged once per zone/state/pattern, not every update.
    private static readonly HashSet<string> LoggedMisses = new();

    private static void CreateWeatherPatternLookup(WeatherRegistry registry)
    {
        try
        {
            PatternsByName.Clear();

            if (registry.ZoneConfigList == null)
            {
                SrLogger.LogDebug("WeatherRegistry.ZoneConfigList is null");
                return;
            }

            foreach (var config in registry.ZoneConfigList)
            {
                if (!config || config.Patterns == null)
                    continue;

                foreach (var pattern in config.Patterns)
                {
                    if (pattern && !string.IsNullOrEmpty(pattern.name))
                        PatternsByName.TryAdd(pattern.name, pattern);
                }
            }

            lookupInitialized = PatternsByName.Count > 0;
            SrLogger.LogDebug($"Weather pattern lookup initialized with {PatternsByName.Count} patterns");
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error in CreateWeatherPatternLookup: {ex}");
            lookupInitialized = false;
        }
    }

    public static void EnsureLookupInitialized()
    {
        if (lookupInitialized)
            return;

        try
        {
            var registry = SceneContext.Instance ? SceneContext.Instance.WeatherRegistry : null;
            if (!registry)
                return;

            CreateWeatherPatternLookup(registry!);
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error in EnsureLookupInitialized: {ex}");
        }
    }

    /// <summary>
    /// Client: the pattern of one of the host's forecast entries. By the name the host sent, first
    /// among the zone's own patterns, then any pattern the game has; only when the name is unknown,
    /// the zone's own pattern that contains the state. Never another zone's pattern by state.
    /// </summary>
    public static WeatherPatternDefinition? ResolvePattern(
        ZoneDefinition zoneKey,
        WeatherRegistry.ZoneWeatherData zone,
        string? patternName,
        WeatherStateDefinition state)
    {
        EnsureLookupInitialized();

        var zonePatterns = zone.Config ? zone.Config.Patterns : null;

        if (!string.IsNullOrEmpty(patternName))
        {
            if (zonePatterns != null)
            {
                foreach (var pattern in zonePatterns)
                {
                    if (pattern && pattern.name == patternName)
                        return pattern;
                }
            }

            if (PatternsByName.TryGetValue(patternName!, out var known) && known)
                return known;

            foreach (var pattern in Resources.FindObjectsOfTypeAll<WeatherPatternDefinition>())
            {
                if (pattern && pattern.name == patternName)
                {
                    PatternsByName[patternName!] = pattern;
                    return pattern;
                }
            }
        }

        if (zonePatterns != null)
        {
            var asState = state.Cast<IWeatherState>();
            foreach (var pattern in zonePatterns)
            {
                try
                {
                    if (pattern && pattern.ContainsState(asState))
                    {
                        LogMissOnce(zoneKey, state, patternName,
                            $"Weather: pattern '{patternName}' from the host is unknown here, using {zoneKey.name}'s own '{pattern.name}' for {state.name}");
                        return pattern;
                    }
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Weather: ContainsState failed on {pattern?.name}: {ex.Message}");
                }
            }
        }

        LogMissOnce(zoneKey, state, patternName,
            $"Weather: no pattern for {zoneKey.name} / {state.name} (host sent '{patternName}'); the entry is left off the map");
        return null;
    }

    private static void LogMissOnce(ZoneDefinition zoneKey, WeatherStateDefinition state, string? patternName, string message)
    {
        if (LoggedMisses.Add($"{zoneKey.name}|{state.name}|{patternName}"))
            SrLogger.LogWarning(message);
    }

    public static void SendWeatherUpdate()
    {
        if (!Main.Server.IsRunning)
            return;

        try
        {
            var weatherRegistry = SceneContext.Instance ? SceneContext.Instance.WeatherRegistry : null;

            if (!weatherRegistry || weatherRegistry!._zones == null)
            {
                SrLogger.LogDebug("SendWeatherUpdate: no weather registry yet");
                return;
            }

            var packet = WeatherPacket.CreateFromRegistry(weatherRegistry, PacketType.WeatherUpdate);
            Main.Server.SendToAll(packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error in SendWeatherUpdate: {ex}");
        }

        // The host's weather changed (or was sent again), so an open cheat menu shows it.
        NetworkCheats.NotifyWeatherChanged();
    }
}