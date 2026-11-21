using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.UI;

// At the end of every scene group load (SceneLoader OnPostSceneGroupLoadedDelegate, after
// IsSceneLoadInProgress went false) the game calls InputDirector.OnSceneGroupLoaded. It picks the
// gameplay or the menu ("paused") input map from Time.timeScale == 0 only. In single player the time
// scale follows TimeDirector's pause counter, so an open map or slimepedia (which pauses) keeps its menu
// controls. In multiplayer ForceTimeScale holds the time scale at 1 (or at 0 while loading), so the game
// turned the menu controls off under an open menu (it could never be closed, and the cursor got locked),
// and could also turn gameplay controls off right after a load with no menu open.
// Here the game's choice is corrected in gameplay scenes only. The menu controls are turned on only when
// a pause is held and a menu is really open, so a leftover pause with no menu (which the forced time
// scale hides in multiplayer) cannot take the player's controls away. In that case the menu controls are
// turned off if the game picked them (it does when it saw the loading time scale, 0), and it is logged.
[HarmonyPatch(typeof(InputDirector), nameof(InputDirector.OnSceneGroupLoaded))]
internal static class InputMapsAfterLoadFix
{
    public static void Postfix(InputDirector __instance)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        try
        {
            var loader = SystemContext.Instance.SceneLoader;
            var gameplay = loader && loader.IsCurrentSceneGroupGameplay();
            var mainGame = __instance._mainGame.Map;
            var paused = __instance._paused.Map;

            // The main menu and loading scenes keep the game's own choice.
            if (!gameplay)
            {
                SrLogger.LogDebug($"Input maps after zone load (not gameplay): mainGame={mainGame.enabled} paused={paused.enabled}");
                return;
            }

            var scene = SceneContext.Instance;
            var time = scene ? scene.TimeDirector : null;
            if (!time)
                return;

            var hasPauser = time!.HasPauser();
            var pausers = time._pauserCount;
            var menuOpen = IsGameMenuOpen(out var which, refreshPedia: true);

            SrLogger.LogDebug($"Input maps after zone load: mainGame={mainGame.enabled} paused={paused.enabled} "
                              + $"timeScale={UnityEngine.Time.timeScale} pausers={pausers} menuOpen={menuOpen} ({which})");

            if (hasPauser && menuOpen)
            {
                // A leftover pauser would keep the game from switching back when this menu closes (it only does so
                // when the pause count reaches 0), so let the Unstuck watcher put the gameplay controls back.
                if (pausers > 1)
                    SR2MP.Components.UI.MultiplayerUI.menuInputFromUnstuck = true;

                if (paused.enabled)
                    return;

                SrLogger.LogWarning($"A menu held the game paused when the zone load finished (pausers={pausers}, open: {which}, "
                                    + $"timeScale={UnityEngine.Time.timeScale}); keeping the menu controls on.");
                // Same as what the game does when it pauses: gameplay map off, menu map on.
                __instance.UpdateInputMaps(true);
                SR2MP.Components.UI.MultiplayerUI.menuInputFromUnstuck = true;
                return;
            }

            if (!hasPauser)
            {
                if (!paused.enabled)
                    return;

                SrLogger.LogWarning("Zone load finished with no pause held but the game picked the menu controls "
                                    + $"(timeScale={UnityEngine.Time.timeScale}); turning gameplay controls back on.");
                // Same as what the game does when it unpauses: menu map off, gameplay map on.
                __instance.UpdateInputMaps(false);
                return;
            }

            // A pause is held but no menu is open: a leftover pauser. In multiplayer the forced time scale
            // hides it, so the only harm would be the menu map: the game picks it when b__18 saw the loading
            // time scale (0). Nothing would ever switch back (the pause count never reaches 0, and F6 only acts
            // when the menu map is off or it turned it on itself), so give the gameplay controls back.
            if (paused.enabled && !MenuEUtil.isAnyMenuOpen && !MenuEUtil.isAnyPopUpOpen)
            {
                SrLogger.LogWarning($"Zone load finished with a pause held but no menu open (pausers={pausers}, "
                                    + $"timeScale={UnityEngine.Time.timeScale}) and the game picked the menu controls; "
                                    + "turning gameplay controls back on.\n" + FreezeLog.Dump());
                __instance.UpdateInputMaps(false);
                return;
            }

            SrLogger.LogWarning($"Zone load finished with a pause held but no menu open (pausers={pausers}, "
                                + $"mainGame={mainGame.enabled} paused={paused.enabled}); leaving the controls as the game set them.\n"
                                + FreezeLog.Dump());
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Input map check after zone load failed: {ex.Message}");
        }
    }

    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PediaOpener>? pediaOpeners;
    private static float pediaOpenersAt = -100f;

    // Real seconds between searches for the slimepedia openers when asked every frame.
    private const float PediaOpenerSearchSeconds = 5f;

    /// <summary>
    /// True while one of the game's own menus is really open: a UI display (map, slimepedia, ...) holds
    /// the game paused (UIDisplayManager._hasPausedGame), or the map UI is up. The slimepedia opener's
    /// own flag is only reported in which, it never counts as an open menu. Cheap enough to call every
    /// frame: the slimepedia openers are only searched for when refreshPedia is set or every few seconds.
    /// </summary>
    internal static bool IsGameMenuOpen(out string which, bool refreshPedia = false)
    {
        var display = UiDisplayHoldsPause();
        var map = MapOpen();
        // The slimepedia opener sets _pediaOpen together with its own pause before the slimepedia is shown,
        // and clears both only at the end of its ShowVac coroutine. If that coroutine is cut short (the opener
        // gets disabled, for example during a zone load) the flag stays set next to a leftover pause with
        // nothing on screen. So it is only reported here. An open slimepedia is a UI display and already
        // shows up through UIDisplayManager._hasPausedGame.
        var pediaFlag = PediaOpen(refreshPedia);
        var open = display || map;

        var parts = new[] { display ? "ui display" : null, map ? "map" : null, pediaFlag ? "slimepedia opener flag" : null }
            .Where(name => name != null).ToArray();
        which = parts.Length == 0 ? "none" : string.Join(", ", parts);
        return open;
    }

    internal static bool IsGameMenuOpen() => UiDisplayHoldsPause() || MapOpen();

    private static bool UiDisplayHoldsPause()
    {
        try
        {
            var ui = UIDisplayManager.Instance; // plain class, lazily created; not a UnityEngine.Object
            return ui != null && ui._hasPausedGame;
        }
        catch
        {
            return false;
        }
    }

    private static bool MapOpen()
    {
        try
        {
            var map = ActiveMapUI;
            return map && map!.isActiveAndEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static bool PediaOpen(bool refresh)
    {
        try
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (refresh || pediaOpeners == null || now - pediaOpenersAt >= PediaOpenerSearchSeconds)
            {
                pediaOpeners = Object.FindObjectsOfType<PediaOpener>();
                pediaOpenersAt = now;
            }

            foreach (var opener in pediaOpeners)
            {
                if (opener && opener._pediaOpen)
                    return true;
            }
        }
        catch
        {
            pediaOpeners = null;
        }

        return false;
    }
}
