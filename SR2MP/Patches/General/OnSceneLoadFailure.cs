using System.Text;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using SR2MP.Shared.Managers;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.General;

// Diagnostics only. When a zone load fails, the game shows an error and can send the player back
// to the main menu, and nothing about the reason reached the mod's log. These write down what the
// game reported, so a kick to the menu can be traced to the step that failed. None of them change
// what the game does.
[HarmonyPatch]
internal static class OnSceneLoadFailure
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.RecordLoadStepFailure))]
    public static void LoadStepPrefix([HarmonyArgument(0)] SceneLoadErrorData errorData)
        => Log("load step", errorData);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.RecordLoadStepFailurePostSceneGroupLoaded))]
    public static void PostLoadPrefix([HarmonyArgument(0)] SceneLoadErrorData errorData)
        => Log("after the zone loaded", errorData);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.OnWorldPopulatorFail))]
    public static void PopulatorPrefix([HarmonyArgument(0)] Il2CppSystem.Exception exception)
    {
        try
        {
            SrLogger.LogError(
                $"The game failed to fill a zone with its actors ({Session()}).\n{Describe(exception)}");
            SceneLoadTrace.Dump("the world populator failed");
        }
        catch
        {
            // ignored
        }
    }

    // Every zone change, so the trace shows where the player was going and how.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadSceneGroup))]
    public static void LoadSceneGroupPrefix(SceneLoader __instance,
        [HarmonyArgument(0)] SceneGroup sceneGroup, [HarmonyArgument(1)] SceneLoadingParameters parameters)
        => LogZoneChange(__instance, sceneGroup, parameters, false);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadSceneGroupAndForceCurrent))]
    public static void LoadSceneGroupForcedPrefix(SceneLoader __instance,
        [HarmonyArgument(0)] SceneGroup sceneGroup, [HarmonyArgument(1)] SceneLoadingParameters parameters)
        => LogZoneChange(__instance, sceneGroup, parameters, true);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadMainMenuSceneGroup))]
    public static void MainMenuPrefix(SceneLoader __instance)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                return;

            var builder = new StringBuilder();
            builder.AppendLine($"Returning to the main menu ({Session()}).");
            builder.AppendLine($"  Current zone: {Name(__instance._currentSceneGroup)}, previous: {Name(__instance._previousSceneGroup)}");
            builder.AppendLine($"  Load in progress: {__instance.IsSceneLoadInProgress}, boat ride: {IsBoatRide(__instance._currentLoadParams)}");
            AppendErrors(builder, __instance);
            builder.AppendLine("  Called from (mod frames show whether the mod started it):");
            builder.AppendLine(System.Environment.StackTrace);

            SrLogger.LogError(builder.ToString());
            SceneLoadTrace.Dump("returning to the main menu");
        }
        catch
        {
            // ignored
        }
    }

    // The game calls this at the end of every zone load, error or not, and it returns at once when
    // nothing is queued. Only a load that really queued errors is worth an error line and a trace.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.HandleSceneLoadingError))]
    public static void HandleErrorPrefix(SceneLoader __instance)
    {
        try
        {
            var queued = __instance._loadErrors;
            if (queued == null || queued.Count == 0)
                return;

            var builder = new StringBuilder();
            builder.AppendLine($"The game is handling a zone load error ({Session()}).");
            AppendErrors(builder, __instance);
            SrLogger.LogError(builder.ToString());
            SceneLoadTrace.Dump("zone load error");
        }
        catch
        {
            // ignored
        }
    }

    private static void LogZoneChange(SceneLoader loader, SceneGroup? target, SceneLoadingParameters? parameters, bool forced)
    {
        try
        {
            var players = PlayerManager.GetAllPlayers().Count;
            var line = $"Zone change: {Name(loader._currentSceneGroup)} -> {Name(target)}" +
                       $" (boat ride: {IsBoatRide(parameters)}, pause: {parameters?.PauseGame.ToString() ?? "?"}" +
                       $"{(forced ? ", forced" : string.Empty)}, {Session()}, players: {players}," +
                       $" held-back changes: {NetworkActorManager.DeferredWorldChangeCount})";

            SceneLoadTrace.Record(line);

            if (Main.Server.IsRunning || Main.Client.IsConnected)
                SrLogger.LogMessage(line);
        }
        catch
        {
            // ignored
        }
    }

    private static void AppendErrors(StringBuilder builder, SceneLoader loader)
    {
        try
        {
            var errors = loader._loadErrors;
            if (errors == null || errors.Count == 0)
            {
                builder.AppendLine("  No load errors queued.");
                return;
            }

            builder.AppendLine($"  {errors.Count} load errors queued:");
            foreach (var error in errors.ToArray())
            {
                if (error == null)
                    continue;

                builder.AppendLine($"  - {error.ResolutionAction}: {Describe(error.Exception)}");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"  The load errors could not be read: {ex.Message}");
        }
    }

    private static string Name(SceneGroup? group)
    {
        try
        {
            return group ? group!.name : "none";
        }
        catch
        {
            return "?";
        }
    }

    private static string IsBoatRide(SceneLoadingParameters? parameters)
    {
        try
        {
            return parameters == null ? "?" : parameters.IsBoatRide.ToString();
        }
        catch
        {
            return "?";
        }
    }

    private static void Log(string where, SceneLoadErrorData? errorData)
    {
        try
        {
            var action = errorData != null ? errorData.ResolutionAction.ToString() : "unknown";
            SrLogger.LogError(
                $"The game reported a failed zone load ({where}, {Session()}), it will: {action}.\n{Describe(errorData?.Exception)}");
            SceneLoadTrace.Dump($"failed zone load, {where}");
        }
        catch
        {
            // ignored
        }
    }

    private static string Session()
        => Main.Server.IsRunning ? "hosting" : Main.Client.IsConnected ? "connected to a host" : "not in multiplayer";

    private static string Describe(Il2CppSystem.Exception? exception)
    {
        if (exception == null)
            return "No exception was attached.";

        try
        {
            return exception.ToString();
        }
        catch
        {
            return "The exception could not be read.";
        }
    }
}
