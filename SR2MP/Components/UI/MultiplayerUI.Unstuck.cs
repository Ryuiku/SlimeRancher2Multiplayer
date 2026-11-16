using System.Text;

namespace SR2MP.Components.UI;

internal sealed partial class MultiplayerUI
{
    // F6: for when a player is stuck in place. Writes what is blocking movement to the log
    // (so the cause can be found) and then tries to free the player.
    private static bool IsInGameplay()
    {
        try
        {
            return SceneContext.Instance
                   && SceneContext.Instance.player
                   && !SystemContext.Instance.SceneLoader.IsSceneLoadInProgress
                   && SystemContext.Instance.SceneLoader.IsCurrentSceneGroupGameplay();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the game is holding the local player in place for sleep or a knock-out.
    /// </summary>
    internal static bool IsLocalPlayerLocked(out double unlockWorldTime)
    {
        unlockWorldTime = 0;

        try
        {
            var playerLock = Il2CppMonomiPark.SlimeRancher.Player.LockOnDeath.Instance;
            if (!playerLock || !playerLock._locked)
                return false;

            unlockWorldTime = playerLock._unlockWorldTime;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private float lockStallSeconds;

    // Game seconds. A lock this short ends by itself soon enough.
    private const double LockStallMinRemaining = 600;
    private const double LockStallMaxSkip = 86400 * 1.5;

    // The game freezes a sleeping or knocked-out player until the clock reaches the wake-up time,
    // and gets there by skipping time. If the skip is not running, the player stands frozen for
    // minutes while everything else carries on. Notice that and finish the skip.
    private void WatchPlayerLock()
    {
        try
        {
            if ((!Main.Server.IsRunning && !Main.Client.IsConnected) || !IsInGameplay()
                || !IsLocalPlayerLocked(out var unlockTime))
            {
                lockStallSeconds = 0;
                return;
            }

            var time = SceneContext.Instance.TimeDirector;
            var remaining = unlockTime - time._worldModel.worldTime;

            if (time.IsFastForwarding() || remaining <= LockStallMinRemaining)
            {
                lockStallSeconds = 0;
                return;
            }

            lockStallSeconds += UnityEngine.Time.unscaledDeltaTime;
            if (lockStallSeconds < 10f)
                return;

            lockStallSeconds = 0;

            if (remaining > LockStallMaxSkip)
            {
                SrLogger.LogWarning($"Player was held in place with {remaining / 3600:F1} game hours left on the clock and no time skip running. Releasing the player.");
                Il2CppMonomiPark.SlimeRancher.Player.LockOnDeath.Instance.Unlock();
                return;
            }

            SrLogger.LogWarning($"Player was held in place (sleep or knock-out) with {remaining / 3600:F1} game hours left and no time skip running. Finishing the time skip.");
            time.FastForwardTo(unlockTime);
        }
        catch (Exception ex)
        {
            lockStallSeconds = 0;
            SrLogger.LogDebug($"Player lock check failed: {ex.Message}");
        }
    }

    private void HandleUnstuck()
    {
        WatchPlayerLock();

        // Chat got hidden while it was still focused or still held the game's input: let go of both.
        // (F5 while typing left the chat "focused" forever, so F4, Enter and this key stopped working.)
        if (chatHidden && (disabledInput || isChatFocused))
        {
            var hadInput = disabledInput;

            isChatFocused = false;
            if (activeInputId == "chat_input")
                activeInputId = string.Empty;
            disabledInput = false;

            if (hadInput && IsInGameplay())
                EnableInput();
        }

        // Only ignore the key while the player is really typing in the chat.
        if (!KeyCode.F6.OnKeyDown() || (isChatFocused && disabledInput && !chatHidden))
            return;

        var report = new StringBuilder("Unstuck (F6) pressed. State before:");

        try
        {
            var input = GameContext.Instance.InputDirector;
            report.Append($"\n  input: mainGame={input._mainGame.Map.enabled} paused={input._paused.Map.enabled}");
            report.Append($"\n  timeScale={UnityEngine.Time.timeScale} sceneLoading={SystemContext.Instance.SceneLoader.IsSceneLoadInProgress}");
            report.Append($"\n  chat: focused={isChatFocused} hidden={chatHidden} disabledInput={disabledInput} activeInput='{activeInputId}'");
            report.Append($"\n  menuOpen={MenuEUtil.isAnyMenuOpen} popupOpen={MenuEUtil.isAnyPopUpOpen}");
            report.Append($"\n  net: hosting={Main.Server.IsRunning} connected={Main.Client.IsConnected}");

            try
            {
                var time = SceneContext.Instance.TimeDirector;
                var locked = IsLocalPlayerLocked(out var unlockTime);
                report.Append($"\n  lock: locked={locked} unlockAt={unlockTime:F0} worldTime={time._worldModel.worldTime:F0} skipping={time.IsFastForwarding()}");
                report.Append($"\n  pause: hasPauser={time.HasPauser()} pausers={time._pauserCount}");
            }
            catch (Exception ex)
            {
                report.Append($"\n  (lock state unavailable: {ex.Message})");
            }

            var player = SceneContext.Instance ? SceneContext.Instance.player : null;
            if (player)
            {
                foreach (var other in PlayerObjects)
                {
                    if (other.Value)
                        report.Append($"\n  other player '{other.Key}': {Vector3.Distance(other.Value.transform.position, player!.transform.position):F1}m away");
                }

                var parent = player!.transform.parent;
                report.Append($"\n  player: pos={player.transform.position} active={player.activeInHierarchy} parent={(parent ? parent.name : "none")}");

                var body = player.GetComponent<Rigidbody>();
                if (body)
                    report.Append($"\n  rigidbody: kinematic={body.isKinematic} constraints={body.constraints} velocity={body.velocity}");

                foreach (var component in player.GetComponents<Component>())
                {
                    if (!component)
                        continue;

                    var behaviour = component.TryCast<Behaviour>();
                    var typeName = component.GetIl2CppType().Name;

                    if (behaviour != null)
                        report.Append($"\n    {typeName}: enabled={behaviour.enabled}");
                    else
                        report.Append($"\n    {typeName}");
                }

                foreach (var joint in player.GetComponents<Joint>())
                    report.Append($"\n  joint on player: {joint.GetIl2CppType().Name} connectedTo={(joint.connectedBody ? joint.connectedBody.name : "none")}");
            }
            else
            {
                report.Append("\n  player: not found");
            }
        }
        catch (Exception ex)
        {
            report.Append($"\n  (report failed: {ex.Message})");
        }

        SrLogger.LogWarning(report.ToString());

        try
        {
            // Held for sleep or a knock-out with no time skip running: let the player go.
            if (IsInGameplay() && IsLocalPlayerLocked(out _) && !SceneContext.Instance.TimeDirector.IsFastForwarding())
            {
                SrLogger.LogWarning("Unstuck: the player was held in place with no time skip running, releasing.");
                Il2CppMonomiPark.SlimeRancher.Player.LockOnDeath.Instance.Unlock();
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Unstuck could not release the player lock: {ex.Message}");
        }

        try
        {
            var input = GameContext.Instance.InputDirector;

            // Not on the main menu or a loading screen: the game keeps its controls off there on purpose.
            if (IsInGameplay() && !MenuEUtil.isAnyMenuOpen && !MenuEUtil.isAnyPopUpOpen && !input._paused.Map.enabled)
            {
                isChatFocused = false;
                activeInputId = string.Empty;
                disabledInput = false;
                input._mainGame.Map.Enable();
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Unstuck failed: {ex.Message}");
        }
    }
}
