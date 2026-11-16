using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;

namespace SR2MP.Patches.General;

// Diagnostics only. When a zone load fails, the game shows an error and can send the player back
// to the main menu, and nothing about the reason reached the mod's log. These write down what the
// game reported, so a kick to the menu can be traced to the step that failed.
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
        }
        catch
        {
            // ignored
        }
    }

    private static void Log(string where, SceneLoadErrorData? errorData)
    {
        try
        {
            var action = errorData != null ? errorData.ResolutionAction.ToString() : "unknown";
            SrLogger.LogError(
                $"The game reported a failed zone load ({where}, {Session()}), it will: {action}.\n{Describe(errorData?.Exception)}");
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
