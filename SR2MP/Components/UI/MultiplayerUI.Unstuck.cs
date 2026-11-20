using System.Text;
using Il2CppKinematicCharacterController;
using Il2CppMonomiPark.SlimeRancher.Cutscene;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.UI.CommStation;
using SR2MP.Components.Actor;
using SR2MP.Shared.Utils;

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

    private static bool InSession => Main.Server.IsRunning || Main.Client.IsConnected;

    /// <summary>
    /// Whether the game is holding the local player in place for sleep or a knock-out.
    /// </summary>
    internal static bool IsLocalPlayerLocked(out double unlockWorldTime)
    {
        unlockWorldTime = 0;

        try
        {
            var playerLock = LockOnDeath.Instance;
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

    private static SRCharacterController? GetLocalController()
    {
        try
        {
            var player = SceneContext.Instance ? SceneContext.Instance.player : null;
            if (!player)
                return null;

            var controller = player!.GetComponent<SRCharacterController>();
            return controller ? controller : player.GetComponentInChildren<SRCharacterController>();
        }
        catch
        {
            return null;
        }
    }

    private static bool HasActiveCutscene()
    {
        try
        {
            var cutscenes = CutsceneDirector.Instance;
            return cutscenes != null && cutscenes.HasActiveCutscene;
        }
        catch
        {
            return false;
        }
    }

    // Looked up only when a watchdog is about to act, never every frame.
    private static bool IsConversationOpen()
    {
        try
        {
            var conversation = UnityEngine.Object.FindObjectOfType<ConversationUI>();
            return conversation && conversation.isActiveAndEnabled;
        }
        catch
        {
            return false;
        }
    }

    // Something on screen that may hold the player on purpose (a menu, a popup, the ranch house
    // screen, typing in the chat). The watchdogs below leave the player alone while it is up.
    private bool IsUiHoldingPlayer()
    {
        try
        {
            return MenuEUtil.isAnyMenuOpen
                   || MenuEUtil.isAnyPopUpOpen
                   || isChatFocused
                   || IsInRanchHouse
                   || GameContext.Instance.InputDirector._paused.Map.enabled;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Lets go of a sleep or knock-out lock, and of the controller freeze that comes with it.
    /// Unlock alone may leave the controller frozen (the game has a separate Unfreeze), which
    /// would look exactly like "held in place, UI still works".
    /// </summary>
    private static void ReleaseLock(string why)
    {
        var playerLock = LockOnDeath.Instance;

        try
        {
            if (playerLock && playerLock._locked)
            {
                SrLogger.LogWarning($"{why}: unlocking the player.");
                playerLock.Unlock();
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"{why}: Unlock failed: {ex.Message}");
        }

        try
        {
            if (playerLock)
                playerLock.Unfreeze();
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"{why}: Unfreeze failed: {ex.Message}");
        }

        try
        {
            var controller = GetLocalController();
            if (controller)
                controller!.SetFreeze(false);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"{why}: unfreezing the controller failed: {ex.Message}");
        }
    }

    private float lockStallSeconds;

    // The world clock while the player is locked, to notice a clock that stopped.
    private double lockClockLast = double.NaN;
    private float lockClockStillSeconds;

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
            if (!InSession || !IsInGameplay() || !IsLocalPlayerLocked(out var unlockTime))
            {
                lockStallSeconds = 0;
                lockClockLast = double.NaN;
                lockClockStillSeconds = 0;
                return;
            }

            var time = SceneContext.Instance.TimeDirector;
            var now = time._worldModel.worldTime;
            var remaining = unlockTime - now;

            // A clock that does not move never reaches the wake-up time, whether a skip is
            // running or not (a skip that runs can still be held by a pauser that leaked).
            if (double.IsNaN(lockClockLast) || Math.Abs(now - lockClockLast) > 0.001)
            {
                lockClockLast = now;
                lockClockStillSeconds = 0;
            }
            else if (!IsUiHoldingPlayer())
            {
                lockClockStillSeconds += UnityEngine.Time.unscaledDeltaTime;
                if (lockClockStillSeconds >= 10f)
                {
                    lockClockStillSeconds = 0;
                    lockStallSeconds = 0;
                    HandleStoppedClockWhileLocked(time, remaining);
                    return;
                }
            }

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
                ReleaseLock("Lock with no skip");
                return;
            }

            SrLogger.LogWarning($"Player was held in place (sleep or knock-out) with {remaining / 3600:F1} game hours left and no time skip running. Finishing the time skip.");
            time.FastForwardTo(unlockTime);
        }
        catch (Exception ex)
        {
            lockStallSeconds = 0;
            lockClockStillSeconds = 0;
            SrLogger.LogDebug($"Player lock check failed: {ex.Message}");
        }
    }

    private static void HandleStoppedClockWhileLocked(TimeDirector time, double remaining)
    {
        var skipping = time.IsFastForwarding();
        var pausers = time._pauserCount;

        SrLogger.LogWarning($"Player is held in place and the world clock has not moved for 10 s "
                            + $"(skip running={skipping}, {remaining / 3600:F1} game hours left, pausers={pausers}).\n"
                            + FreezeLog.Dump());

        // No menu is open (checked by the caller), so a pauser left over is what stops the clock.
        if (time.HasPauser() && pausers > 0)
        {
            SrLogger.LogWarning($"Removing {pausers} time pauser(s) that no open menu accounts for.");
            for (var i = 0; i < pausers && time.HasPauser(); i++)
                time.Unpause();
            return;
        }

        if (skipping)
        {
            try
            {
                SrLogger.LogWarning("Ending the time skip that does not move the clock.");
                time.ForceFastForwardEnd();
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not end the time skip: {ex.Message}");
            }
        }

        ReleaseLock("Clock stopped while locked");
    }

    private float staleFreezeLastTry = -999f;
    private float staleFreezeFreeSeconds;
    private const float StaleFreezeSeconds = 30f;

    // The controller was frozen with SetFreeze(true) and nothing that freezes on purpose (a lock,
    // a cutscene, a zone load, a time skip, a menu or conversation) is still going on.
    private void WatchStaleFreeze()
    {
        try
        {
            if (!FreezeLog.ControllerFrozen || !InSession || !IsInGameplay())
            {
                staleFreezeFreeSeconds = 0;
                return;
            }

            if (IsUiHoldingPlayer() || IsLocalPlayerLocked(out _) || HasActiveCutscene()
                || SceneContext.Instance.TimeDirector.IsFastForwarding())
            {
                staleFreezeFreeSeconds = 0;
                return;
            }

            // Give the game a few seconds of its own after a lock or cutscene ends (wake-up).
            staleFreezeFreeSeconds += UnityEngine.Time.unscaledDeltaTime;

            var now = UnityEngine.Time.realtimeSinceStartup;
            var frozenFor = now - FreezeLog.FrozenSince;
            if (frozenFor < StaleFreezeSeconds || staleFreezeFreeSeconds < 5f
                || now - staleFreezeLastTry < StaleFreezeSeconds)
                return;

            staleFreezeLastTry = now;

            if (IsConversationOpen())
                return;

            var controller = GetLocalController();
            if (!controller)
                return;

            SrLogger.LogWarning($"Player controller was frozen {frozenFor:F0} s ago {FreezeLog.FrozenBy} and nothing holds it any more, unfreezing.\n"
                                + FreezeLog.Dump());
            controller!.SetFreeze(false);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Stale freeze check failed: {ex.Message}");
        }
    }

    private Vector3 stallAnchor;
    private float stallSeconds;
    private bool stallReported;
    private bool stallAnnounced;
    private float stallLastReport = -999f;

    // Only writes to the log. Pressing to walk while not moving at all for a few seconds is what
    // a freeze looks like from the inside; walking into a wall looks the same, so nothing is changed.
    private void WatchMovementStall()
    {
        try
        {
            var controller = InSession && IsInGameplay() ? GetLocalController() : null;
            if (!controller || IsUiHoldingPlayer() || IsLocalPlayerLocked(out _)
                || SceneContext.Instance.TimeDirector.IsFastForwarding())
            {
                ResetStall(controller);
                return;
            }

            var position = controller!.transform.position;
            if (controller.InputVector.magnitude <= 0.5f || (position - stallAnchor).magnitude >= 0.05f)
            {
                ResetStall(controller);
                return;
            }

            stallSeconds += UnityEngine.Time.unscaledDeltaTime;

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (!stallReported && stallSeconds >= 4f && now - stallLastReport >= 60f)
            {
                stallReported = true;
                stallLastReport = now;

                if (HasActiveCutscene())
                    return;

                SrLogger.LogWarning(BuildStuckReport("Movement stalled: walking input for 4 s but the player did not move (a wall can cause this too)."));
            }

            // Only tell the player when it lasts; nobody walks into a wall for this long.
            if (stallReported && !stallAnnounced && stallSeconds >= 10f)
            {
                stallAnnounced = true;
                RegisterSystemMessage("Movement looks stuck, press F6",
                    $"SYSTEM_MOVEMENT_STALL_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", SystemMessageNormal);
            }
        }
        catch (Exception ex)
        {
            stallSeconds = 0;
            SrLogger.LogDebug($"Movement stall check failed: {ex.Message}");
        }
    }

    private void ResetStall(SRCharacterController? controller)
    {
        stallSeconds = 0;
        stallReported = false;
        stallAnnounced = false;

        if (controller)
            stallAnchor = controller!.transform.position;
    }

    private string BuildStuckReport(string header)
    {
        var report = new StringBuilder(header);

        try
        {
            var input = GameContext.Instance.InputDirector;
            report.Append($"\n  input: mainGame={input._mainGame.Map.enabled} paused={input._paused.Map.enabled}");
            report.Append($" uiPaused={SR2MP.Patches.UI.InputMapsAfterLoadFix.IsGameMenuOpen(out var openMenus, refreshPedia: true)} ({openMenus})");
            report.Append($"\n  timeScale={UnityEngine.Time.timeScale} sceneLoading={SystemContext.Instance.SceneLoader.IsSceneLoadInProgress}");
            report.Append($"\n  chat: focused={isChatFocused} hidden={chatHidden} disabledInput={disabledInput} activeInput='{activeInputId}'");
            report.Append($"\n  menuOpen={MenuEUtil.isAnyMenuOpen} popupOpen={MenuEUtil.isAnyPopUpOpen} ranchHouse={IsInRanchHouse}");
            report.Append($"\n  net: hosting={Main.Server.IsRunning} connected={Main.Client.IsConnected}");

            try
            {
                var loader = SystemContext.Instance.SceneLoader;
                var actions = loader._loadActions;
                report.Append($"\n  sceneLoader: processingQueue={loader._isProcessingQueue} loadActions={(actions != null ? actions.Count : 0)}");
            }
            catch (Exception ex)
            {
                report.Append($"\n  (scene loader state unavailable: {ex.Message})");
            }

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

            report.Append($"\n  freeze: controllerFrozen={FreezeLog.ControllerFrozen}"
                          + (FreezeLog.ControllerFrozen ? $" for {UnityEngine.Time.realtimeSinceStartup - FreezeLog.FrozenSince:F0} s {FreezeLog.FrozenBy}" : string.Empty)
                          + $" cutscene={HasActiveCutscene()}");

            AppendControllerState(report);

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

                AppendHeldItem(report);
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

        report.Append('\n').Append(FreezeLog.Dump());
        return report.ToString();
    }

    private static void AppendControllerState(StringBuilder report)
    {
        try
        {
            var controller = GetLocalController();
            if (!controller)
            {
                report.Append("\n  controller: not found");
                return;
            }

            report.Append($"\n  controller: enabled={controller!.enabled} input={controller.InputVector} grounded={controller.IsGrounded}");

            var deltaOverride = controller._deltaTimeOverride;
            var fixedOverride = controller._fixedDeltaTimeOverride;
            var timeOverride = controller._currentTimeOverride;
            report.Append("\n  time overrides:"
                          + $" delta={(deltaOverride != null && deltaOverride.HasValue ? deltaOverride.Value.ToString("F4") : "none")}"
                          + $" fixedDelta={(fixedOverride != null && fixedOverride.HasValue ? fixedOverride.Value.ToString("F4") : "none")}"
                          + $" current={(timeOverride != null && timeOverride.HasValue ? timeOverride.Value.ToString("F1") : "none")}");

            var motor = controller.Motor;
            if (motor)
            {
                var registered = false;
                var autoSimulation = false;
                try
                {
                    // Static in this game version; a motor that is off is taken out of this list.
                    var motors = KinematicCharacterSystem.CharacterMotors;
                    registered = motors != null && motors.Contains(motor);
                    var settings = KinematicCharacterSystem.Settings;
                    autoSimulation = settings && settings!.AutoSimulation;
                }
                catch
                {
                    // shown as false
                }

                report.Append($"\n  motor: enabled={motor!.enabled} registered={registered} autoSimulation={autoSimulation}");
            }
            else
            {
                report.Append("\n  motor: not found");
            }
        }
        catch (Exception ex)
        {
            report.Append($"\n  (controller state unavailable: {ex.Message})");
        }
    }

    private static void AppendHeldItem(StringBuilder report)
    {
        try
        {
            var vacuum = SceneContext.Instance.PlayerState.VacuumItem;
            var held = vacuum ? vacuum._held : null;
            if (!held)
            {
                report.Append("\n  vac holds: nothing");
                return;
            }

            var networkActor = held!.GetComponent<NetworkActor>();
            var body = held.GetComponent<Rigidbody>();
            report.Append($"\n  vac holds: {held.name}"
                          + (networkActor ? $" owner={(networkActor!.LocallyOwned ? "me" : networkActor.CurrentOwnerId)}" : " (not networked)")
                          + (body ? $" constraints={body!.constraints}" : string.Empty));
        }
        catch (Exception ex)
        {
            report.Append($"\n  (held item unavailable: {ex.Message})");
        }
    }

    // Set when F6 (or InputMapsAfterLoadFix after a zone load) turned the menu controls on for an open menu.
    // A leftover pauser keeps the game from switching back when that menu closes (it only does so when the
    // pause count reaches zero), so the gameplay controls are put back here once no menu is open any more.
    internal static bool menuInputFromUnstuck;

    private void WatchUnstuckMenuInput()
    {
        if (!menuInputFromUnstuck)
            return;

        try
        {
            var input = GameContext.Instance.InputDirector;
            if (!IsInGameplay() || !input._paused.Map.enabled)
            {
                menuInputFromUnstuck = false;
                return;
            }

            if (SR2MP.Patches.UI.InputMapsAfterLoadFix.IsGameMenuOpen() || MenuEUtil.isAnyMenuOpen || MenuEUtil.isAnyPopUpOpen)
                return;

            menuInputFromUnstuck = false;
            SrLogger.LogWarning("Unstuck: the menu that F6 gave controls to has closed but the game kept the menu controls on; turning gameplay controls back on.");
            input.UpdateInputMaps(false);
        }
        catch (Exception ex)
        {
            menuInputFromUnstuck = false;
            SrLogger.LogWarning($"Unstuck: putting gameplay controls back failed: {ex.Message}");
        }
    }

    private void HandleUnstuck()
    {
        WatchPlayerLock();
        WatchStaleFreeze();
        WatchMovementStall();
        WatchUnstuckMenuInput();

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

        SrLogger.LogWarning(BuildStuckReport("Unstuck (F6) pressed. State before:"));

        try
        {
            // Held for sleep or a knock-out: let the player go. A skip that is still running is
            // ended first, or the lock would only come back when it finishes.
            if (IsInGameplay() && IsLocalPlayerLocked(out _))
            {
                var time = SceneContext.Instance.TimeDirector;
                if (time.IsFastForwarding())
                {
                    SrLogger.LogWarning("Unstuck: a time skip was still running, ending it.");
                    time.ForceFastForwardEnd();
                }

                ReleaseLock("Unstuck (F6)");
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Unstuck could not release the player lock: {ex.Message}");
        }

        UnstuckController();

        try
        {
            var input = GameContext.Instance.InputDirector;
            var scene = SceneContext.Instance;
            var time = scene ? scene.TimeDirector : null;

            // A game menu (map, slimepedia) holds the game paused but its controls are off: give them back, so it can be closed.
            if (IsInGameplay() && time && time!.HasPauser()
                && SR2MP.Patches.UI.InputMapsAfterLoadFix.IsGameMenuOpen(out var openMenu, refreshPedia: true)
                && !input._paused.Map.enabled)
            {
                SrLogger.LogWarning($"Unstuck: a menu is open ({openMenu}) but its controls were off; turning menu controls back on.");
                input.UpdateInputMaps(true);
                menuInputFromUnstuck = true;
            }
            // The menu controls are on but no menu is open: F6 or the after-load fix turned them on for a menu
            // that is gone now, or a leftover pause kept the game from switching back. Go back to gameplay controls.
            // (Only on F6: the per-frame watcher stays gated on the flag, because the slimepedia opener holds the
            // game paused for a moment before its display is shown.)
            else if (IsInGameplay() && input._paused.Map.enabled
                     && !SR2MP.Patches.UI.InputMapsAfterLoadFix.IsGameMenuOpen(out _, refreshPedia: true)
                     && !MenuEUtil.isAnyMenuOpen && !MenuEUtil.isAnyPopUpOpen)
            {
                var fromUnstuck = menuInputFromUnstuck;
                menuInputFromUnstuck = false;
                SrLogger.LogWarning(fromUnstuck
                    ? "Unstuck: no menu is open any more; turning gameplay controls back on."
                    : "Unstuck: menu controls were on with no menu open (leftover pause?); turning gameplay controls back on.");
                input.UpdateInputMaps(false);
            }
            // Not on the main menu or a loading screen: the game keeps its controls off there on purpose.
            else if (IsInGameplay() && !MenuEUtil.isAnyMenuOpen && !MenuEUtil.isAnyPopUpOpen && !input._paused.Map.enabled)
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

    // The character controller's own ways to stop moving. Each is put back only when it was off,
    // and only outside a cutscene, which freezes the player on purpose.
    private static void UnstuckController()
    {
        if (!IsInGameplay() || HasActiveCutscene())
            return;

        var controller = GetLocalController();
        if (!controller)
            return;

        try
        {
            if (FreezeLog.ControllerFrozen)
                SrLogger.LogWarning($"Unstuck: the controller was frozen {FreezeLog.FrozenBy}, unfreezing.");

            // Harmless when it was not frozen; the game keeps no flag that could be checked first.
            controller!.SetFreeze(false);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Unstuck: unfreezing the controller failed: {ex.Message}");
        }

        try
        {
            var motor = controller!.Motor;
            if (motor && !motor!.enabled)
            {
                SrLogger.LogWarning("Unstuck: the character motor was off, turning it on.");
                motor.enabled = true;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Unstuck: turning the motor on failed: {ex.Message}");
        }

        try
        {
            var deltaOverride = controller!._deltaTimeOverride;
            var fixedOverride = controller._fixedDeltaTimeOverride;
            var timeOverride = controller._currentTimeOverride;
            if ((deltaOverride != null && deltaOverride.HasValue)
                || (fixedOverride != null && fixedOverride.HasValue)
                || (timeOverride != null && timeOverride.HasValue))
            {
                SrLogger.LogWarning("Unstuck: the controller had a time override, removing it.");
                controller.SetTimeOverrideEnabled(false, new Il2CppSystem.Nullable<float>(), new Il2CppSystem.Nullable<float>());
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Unstuck: removing the time override failed: {ex.Message}");
        }
    }
}
