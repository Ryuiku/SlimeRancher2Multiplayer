using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Handlers.TreasurePod;
using SR2MP.Patches.TreasurePod;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Labyrinth;

// The reward behind a Labyrinth lock or shadow plort depositor. Only the player who solved it
// gives it out; everyone else is told through the pod packet, which records it as opened in the
// host's world and shares the appearance and blueprint.
[HarmonyPatch(typeof(TreasurePodRewarder), nameof(TreasurePodRewarder.Activate))]
internal static class OnRewarderActivate
{
    private static readonly HashSet<string> WarnedNotInPods = new();

    public static bool Prefix(TreasurePodRewarder __instance, out bool __state)
    {
        // Harmony still runs the postfix after a skipped Activate; this tells it nothing happened.
        __state = false;

        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            {
                __state = true;
                return true;
            }

            var id = __instance._id ?? string.Empty;

            if (HandlingPacket)
            {
                SrLogger.LogDebug($"Skipped rewarder {id}: a packet is being applied");
                return false;
            }

            if (NetworkRewarders.IsSuppressed(id))
            {
                SrLogger.LogDebug($"Skipped rewarder {id}: another player solved its lock");
                return false;
            }

            if (TreasurePodHandler.RemotelyOpened.Contains(id))
            {
                SrLogger.LogDebug($"Skipped rewarder {id}: another player already opened it");
                return false;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not check rewarder before it ran: {ex.Message}");
        }

        __state = true;
        return true;
    }

    public static void Postfix(TreasurePodRewarder __instance, bool __state)
    {
        if (!__state || HandlingPacket)
            return;
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        try
        {
            var id = __instance._id;
            if (string.IsNullOrEmpty(id))
                return;

            // Receivers record it in the pods; the in-game test should tell whether the game keeps
            // rewarders there too.
            if (!GameState.pods.ContainsKey(id) && WarnedNotInPods.Add(id))
                SrLogger.LogWarning($"Rewarder {id} is not stored with the pods; sending it as a pod anyway");

            OnTreasurePodOpen.SendOpened(id,
                __instance.UnlockedSlimeAppearanceDefinition,
                __instance.UnlockedSlimeAppearance,
                __instance.Blueprint);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a Labyrinth reward: {ex.Message}");
        }
    }
}
