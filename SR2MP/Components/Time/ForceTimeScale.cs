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

        if (GameContext.Instance.InputDirector._paused.Map.enabled)
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
        UnityEngine.Time.timeScale = loading ? LoadingTimeScale : TimeScale;
    }

    private static bool boatRideLogged;

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