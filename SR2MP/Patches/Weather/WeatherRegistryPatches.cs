using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Client.Managers;
using SR2MP.Server.Managers;

namespace SR2MP.Patches.Weather;

[HarmonyPatch(typeof(WeatherRegistry))]
internal static class WeatherRegistryPatches
{
    [HarmonyPatch(nameof(WeatherRegistry.Update)), HarmonyPrefix]
    public static bool UpdatePrefix() => !Main.Client.IsConnected || Main.Server.IsRunning;

    [HarmonyPatch(nameof(WeatherRegistry.RunPatternState)), HarmonyPrefix]
    public static bool RunPatternStatePrefix()
    {
        WeatherUpdateHelper.EnsureLookupInitialized();
        return !Main.Client.IsConnected || Main.Server.IsRunning || HandlingPacket;
    }

    [HarmonyPatch(nameof(WeatherRegistry.RunPatternState)), HarmonyPostfix]
    public static void RunPatternStatePostfix()
    {
        if (Main.Server.IsRunning && !HandlingPacket)
        {
            WeatherUpdateHelper.SendWeatherUpdate();
        }
    }

    [HarmonyPatch(nameof(WeatherRegistry.StopPatternState)), HarmonyPrefix]
    public static bool StopPatternStatePrefix(WeatherRegistry __instance, ZoneDefinition zone)
    {
        WeatherUpdateHelper.EnsureLookupInitialized();

        if (!zone)
            return false;

        return !Main.Client.IsConnected || Main.Server.IsRunning || HandlingPacket;
    }

    [HarmonyPatch(nameof(WeatherRegistry.StopPatternState)), HarmonyPostfix]
    public static void StopPatternStatePostfix()
    {
        if (Main.Server.IsRunning && !HandlingPacket)
        {
            WeatherUpdateHelper.SendWeatherUpdate();
        }
    }

    // Client: the zone just loaded. The host's running states go on its director (see NetworkWeatherManager).
    [HarmonyPatch(nameof(WeatherRegistry.RegisterDirector)), HarmonyPostfix]
    public static void RegisterDirectorPostfix(ZoneDefinition zone)
    {
        if (!Main.Client.IsConnected || Main.Server.IsRunning)
            return;

        try
        {
            NetworkWeatherManager.OnZoneDirectorRegistered(zone);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Weather: starting the host's weather in a loaded zone failed: {ex.Message}");
        }
    }

    // The client's forecast and ForecastUnlocked are the host's (NetworkWeatherManager), so the game's
    // own map calculation gives the client the same icons the host sees, and an empty result is the
    // game's real answer (for example, the next entry is outside the window the map shows). Only if
    // the calculation throws are the icons filled from the synced forecast instead.
    private static bool loggedMapDataFailure;

    [HarmonyPatch(nameof(WeatherRegistry.CalculateZoneMapData)), HarmonyFinalizer]
    public static Exception? CalculateZoneMapDataFinalizer(Exception? __exception, WeatherRegistry __instance, ZoneMapData zoneMapData, CppCollections.List<ZoneWeatherMapData> mapData)
    {
        if (__exception == null)
            return null;

        if (!loggedMapDataFailure)
        {
            loggedMapDataFailure = true;
            SrLogger.LogWarning($"Map weather: the game's own calculation failed, using the synced forecast instead ({__exception.Message})");
        }

        try
        {
            if (zoneMapData != null)
                FillFromForecast(__instance, zoneMapData, mapData);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"CalculateZoneMapData fallback failed: {ex.Message}");
        }

        return null;
    }

    private static void FillFromForecast(WeatherRegistry registry, ZoneMapData zoneMapData, CppCollections.List<ZoneWeatherMapData> mapData)
    {
        if (mapData == null || mapData.Count > 0)
            return;

        var zoneDef = zoneMapData.WeatherZone ? zoneMapData.WeatherZone : zoneMapData.PrimaryZone;
        if (!zoneDef)
            return;

        if (!registry._zones.TryGetValue(zoneDef, out var zoneWeatherData) || zoneWeatherData == null)
            return;

        if (!zoneWeatherData.ForecastUnlocked)
            return;

        var forecastList = zoneWeatherData.Forecast;
        if (forecastList == null)
            return;

        var excluded = zoneMapData.ExcludedWeatherPatterns;
        var now = SceneContext.Instance ? SceneContext.Instance.TimeDirector._worldModel.worldTime : 0d;

        foreach (var forecast in forecastList)
        {
            if (forecast == null || !forecast.Pattern || !forecast.Pattern.Metadata || forecast.State == null)
                continue;

            if (forecast.IsEndTimeKnown && forecast.EndTime <= now)
                continue;

            if (excluded != null && excluded.Contains(forecast.Pattern))
                continue;

            var present = false;
            foreach (var existing in mapData)
            {
                if (existing.Metadata != forecast.Pattern.Metadata)
                    continue;

                present = true;
                break;
            }

            if (present)
                continue;

            var stateDef = forecast.State.TryCast<WeatherStateDefinition>();
            mapData.Add(new ZoneWeatherMapData
            {
                Metadata = forecast.Pattern.Metadata,
                MapTier = stateDef ? stateDef!.MapTier : forecast.State.GetMapTier()
            });
        }
    }
}