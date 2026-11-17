using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Event;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Map;

// A zone unlock (first visit) is a record in the event table. Tell the others, so the zone shows
// on their map too.
[HarmonyPatch(typeof(EventRecordModel), nameof(EventRecordModel.UpdateEventEntry))]
internal static class OnZoneUnlocked
{
    public static void Postfix([HarmonyArgument(0)] IGameEvent gameEvent)
    {
        if (HandlingPacket || gameEvent == null || !NetworkActorManager.CanSendActorChanges)
            return;

        try
        {
            var eventKey = gameEvent.EventKey;
            if (eventKey != SR2MP.Shared.Utils.MapEvents.ZoneEventKey)
                return;

            var dataKey = gameEvent.DataKey;
            if (string.IsNullOrEmpty(dataKey))
                return;

            Main.SendToAllOrServer(new MapUnlockPacket { NodeID = dataKey, EventKey = eventKey });
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"OnZoneUnlocked failed: {ex.Message}");
        }
    }
}
