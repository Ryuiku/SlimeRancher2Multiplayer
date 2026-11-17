using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Event;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Events;

[HarmonyPatch(typeof(EventDirector), nameof(EventDirector.RaiseEvent))]
internal static class OnEventRaised
{
    private static bool errorReported;

    // Todo: Causes lags under **VERY SPECIFIC CIRCUMSTANCES**
    public static void Postfix(IGameEvent gameEvent, int count)
    {
        if (gameEvent == null) return;

        // This is intentionally left behind
        // SrLogger.LogDebug($"GameEvent raised: EventKey='{gameEvent.EventKey}' DataKey='{gameEvent.DataKey}' Count={count}");

        if (HandlingPacket) return;
        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        // Events are raised all through zone loads (arriving in the Sanctuary raises several); an
        // exception leaving here would break the load step that raised it.
        try
        {
            var eventKey = gameEvent.EventKey;
            if (string.IsNullOrEmpty(eventKey)) return;

            if (!NetworkEventManager.HasProducer(eventKey))
                return;

            if (!NetworkEventManager.ShouldSync(eventKey, gameEvent.DataKey)) return;

            NetworkEventManager.SendEventRaised(eventKey, gameEvent.DataKey);
        }
        catch (Exception ex)
        {
            SR2MP.Shared.Utils.SceneLoadTrace.Record($"Sharing a game event threw: {ex.Message}");
            if (!errorReported)
            {
                errorReported = true;
                SrLogger.LogWarning($"Could not share a game event (reported once): {ex}");
            }
        }
    }
}