using MelonLoader;
using Starlight.Storage;

namespace SR2MP.Components.Time;

[InjectIntoIL]
internal sealed class ForceTimeScale : MonoBehaviour
{
    public float TimeScale = 1f;
    public float LoadingTimeScale;

    public void Update()
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        var input = GameContext.Instance.InputDirector;

        // Free while a game menu is open too, so a menu whose controls got switched off can still be
        // closed with the mouse.
        if (input._paused.Map.enabled || SR2MP.Patches.UI.InputMapsAfterLoadFix.IsGameMenuOpen())
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        var loader = SystemContext.Instance.SceneLoader;
        var loading = loader.IsSceneLoadInProgress;
        var loadSeconds = TrackLoad(loader, loading);

        WatchMenuInput(input, loading);

        // The boat ride to the Sanctuary plays while the Sanctuary loads. Holding time at the
        // loading value froze the ride, and the load waiting on it may never finish, which is a
        // suspect for the host being sent to the main menu. The game drives time for that load.
        if (loading && IsBoatRide(loader))
        {
            if (!boatRideLogged)
            {
                boatRideLogged = true;
                SrLogger.LogMessage("Boat ride: the game keeps its own time scale while it loads");
            }

            return;
        }

        boatRideLogged = false;

        // A load that never finishes would otherwise hold time at zero for good: everything stands
        // still and the player cannot move. After a while let time run again until it ends.
        if (loading && loadSeconds >= MaxLoadingSeconds)
            loading = false;

        UnityEngine.Time.timeScale = loading ? LoadingTimeScale : TimeScale;
    }

    private static bool boatRideLogged;

    // A pause is held (TimeDirector pauser) but the menu input map is off and the gameplay map is on.
    // With a menu open it cannot be closed. Only logged, once per episode, so a test shows it happened.
    private static float menuInputBadSince = -1f;
    private static bool menuInputBadLogged;

    private static void WatchMenuInput(Il2CppMonomiPark.SlimeRancher.Input.InputDirector input, bool loading)
    {
        try
        {
            var scene = SceneContext.Instance;
            var time = scene ? scene.TimeDirector : null;
            var bad = !loading && time && time!.HasPauser()
                      && !input._paused.Map.enabled && input._mainGame.Map.enabled;

            if (!bad)
            {
                menuInputBadSince = -1f;
                menuInputBadLogged = false;
                return;
            }

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (menuInputBadSince < 0f)
            {
                menuInputBadSince = now;
                return;
            }

            if (menuInputBadLogged || now - menuInputBadSince < 2f)
                return;

            menuInputBadLogged = true;
            var menuOpen = SR2MP.Patches.UI.InputMapsAfterLoadFix.IsGameMenuOpen(out var which, refreshPedia: true);
            SrLogger.LogWarning(menuOpen
                ? $"Menu controls are off while a menu holds the game paused (pausers={time!._pauserCount}, open: {which}, "
                  + $"timeScale={UnityEngine.Time.timeScale}). The open menu cannot be closed; F6 gives its controls back."
                : $"A pause is held with no menu open while gameplay controls are on (pausers={time!._pauserCount}, "
                  + $"timeScale={UnityEngine.Time.timeScale}). A pauser may have been left behind.");
        }
        catch
        {
            // diagnostics only
        }
    }

    // Real seconds.
    private const float MaxLoadingSeconds = 45f;
    private const float SlowLoadSeconds = 10f;

    private static float loadStartedAt = -1f;
    private static bool longLoadLogged;

    private static float TrackLoad(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader loader, bool loading)
    {
        var now = UnityEngine.Time.realtimeSinceStartup;

        if (!loading)
        {
            if (loadStartedAt >= 0f)
            {
                var took = now - loadStartedAt;
                if (took > SlowLoadSeconds)
                    SrLogger.LogMessage($"Zone load finished after {took:F0} s");
            }

            loadStartedAt = -1f;
            longLoadLogged = false;
            return 0f;
        }

        if (loadStartedAt < 0f)
            loadStartedAt = now;

        var seconds = now - loadStartedAt;
        if (seconds >= MaxLoadingSeconds && !longLoadLogged)
        {
            longLoadLogged = true;
            try
            {
                var group = loader._currentSceneGroup;
                var actions = loader._loadActions;
                SrLogger.LogWarning($"Zone load still running after {seconds:F0} s (scene group {(group ? group.name : "none")}, "
                                    + $"processing queue={loader._isProcessingQueue}, queued load actions={(actions != null ? actions.Count : 0)}). "
                                    + "Letting time run again until it ends.");
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Zone load still running after {seconds:F0} s ({ex.Message}). Letting time run again until it ends.");
            }
        }

        return seconds;
    }

    private static bool IsBoatRide(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader loader)
    {
        try
        {
            var parameters = loader._currentLoadParams;
            return parameters != null && parameters.IsBoatRide;
        }
        catch
        {
            return false;
        }
    }
}