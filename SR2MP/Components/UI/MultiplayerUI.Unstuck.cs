using System.Text;

namespace SR2MP.Components.UI;

internal sealed partial class MultiplayerUI
{
    // F6: for when a player is stuck in place. Writes what is blocking movement to the log
    // (so the cause can be found) and then tries to free the player.
    private void HandleUnstuck()
    {
        // Chat got hidden while it still held the game's input: give the input back.
        if (disabledInput && chatHidden)
        {
            isChatFocused = false;
            activeInputId = string.Empty;
            EnableInput();
            disabledInput = false;
        }

        if (!KeyCode.F6.OnKeyDown() || isChatFocused)
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

            var player = SceneContext.Instance ? SceneContext.Instance.player : null;
            if (player)
            {
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
            var input = GameContext.Instance.InputDirector;

            if (!MenuEUtil.isAnyMenuOpen && !MenuEUtil.isAnyPopUpOpen && !input._paused.Map.enabled)
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
