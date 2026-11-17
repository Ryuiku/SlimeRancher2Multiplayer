using HarmonyLib;
using SR2MP.Handlers.TreasurePod;
using SR2MP.Packets.Gadget;
using SR2MP.Packets.TreasurePod;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.TreasurePod;

[HarmonyPatch(typeof(Il2Cpp.TreasurePod), nameof(Il2Cpp.TreasurePod.Activate))]
internal static class OnTreasurePodOpen
{
    // Activate is what gives out the pod's rewards. A pod that another player already opened (or
    // a pod packet being applied) must never run it here, or its loot would exist twice.
    public static bool Prefix(Il2Cpp.TreasurePod __instance, out bool __state)
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

            if (HandlingPacket || TreasurePodHandler.RemotelyOpened.Contains(__instance._id ?? string.Empty))
            {
                SrLogger.LogDebug($"Skipped opening pod {__instance._id}: another player already opened it");
                return false;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not check pod before opening: {ex.Message}");
        }

        __state = true;
        return true;
    }

    public static void Postfix(Il2Cpp.TreasurePod __instance, bool __state)
    {
        if (!__state || HandlingPacket || string.IsNullOrEmpty(__instance._id))
            return;

        TreasurePodHandler.LocalOpenTimes[__instance._id] = UnityEngine.Time.time;

        // Tells the in-game test whether Activate queues the loot (which the same-moment tie-break
        // in TreasurePodHandler relies on) or spawns it straight away.
        try
        {
            var queue = __instance._model?.spawnQueue;
            SrLogger.LogDebug($"Pod {__instance._id} opened here, {(queue != null ? queue.Count : -1)} item(s) queued");
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not read pod {__instance._id} loot queue: {ex.Message}");
        }

        SlimeDefinition? slime = null;
        SlimeAppearance? appearance = null;
        GadgetDefinition? blueprint = null;
        try
        {
            slime = __instance.UnlockedSlimeAppearanceDefinition;
            appearance = __instance.UnlockedSlimeAppearance;
            blueprint = __instance.Blueprint;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not read the pod's rewards: {ex.Message}");
        }

        SendOpened(__instance._id, slime, appearance, blueprint);
    }

    /// <summary>
    /// Tells everyone that a pod (or a Labyrinth reward that is stored like one) was opened here,
    /// with the rewards that have no packet of their own.
    /// </summary>
    internal static void SendOpened(string id, SlimeDefinition? slime, SlimeAppearance? appearance, GadgetDefinition? blueprint)
    {
        if (string.IsNullOrEmpty(id))
            return;

        var packet = new TreasurePodPacket()
        {
            ID = id,
            OpenerId = LocalID
        };

        // The other players only mark the pod as opened, they do not run its rewards. A slime
        // appearance has no packet of its own, so it travels with the pod.
        try
        {
            if (slime && appearance)
            {
                packet.AppearanceSlime = NetworkActorManager.GetPersistentID(slime);
                packet.AppearanceSet = appearance!.SaveSet;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not read the pod's appearance reward: {ex.Message}");
        }

        Main.SendToAllOrServer(packet);

        // The blueprint is sent from here as well, because the game does not add a blueprint the
        // opener already has, and then nobody else would ever get it from this pod.
        try
        {
            if (blueprint && (Main.Server.IsRunning || Main.Client.IsConnected))
            {
                var blueprints = new BlueprintsPacket();
                blueprints.Blueprints.Add(NetworkActorManager.GetPersistentID(blueprint));
                Main.SendToAllOrServer(blueprints);
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share the pod's blueprint: {ex.Message}");
        }
    }
}