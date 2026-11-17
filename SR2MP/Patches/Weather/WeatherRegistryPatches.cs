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

    // The client's forecast is the host's (NetworkWeatherManager), so the game's own map calculation
    // normally works there. Should it throw, or find nothing for an unlocked zone that has a forecast,
    // the icons are filled from the synced forecast instead. The host's map is left to the game.
    private static bool loggedMapDataFailure;

    private static bool IsClientOnly => Main.Client.IsConnected && !Main.Server.IsRunning;

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

    [HarmonyPatch(nameof(WeatherRegistry.CalculateZoneMapData)), HarmonyPostfix]
    public static void CalculateZoneMapDataPostfix(WeatherRegistry __instance, ZoneMapData zoneMapData, CppCollections.List<ZoneWeatherMapData> mapData)
    {
        if (!IsClientOnly || zoneMapData == null)
            return;

        try
        {
            FillFromForecast(__instance, zoneMapData, mapData);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"CalculateZoneMapData fill failed: {ex.Message}");
        }
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