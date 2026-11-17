using HarmonyLib;
using SR2MP.Packets.Gadget;
using SR2MP.Packets.TreasurePod;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.TreasurePod;

[HarmonyPatch(typeof(Il2Cpp.TreasurePod), nameof(Il2Cpp.TreasurePod.Activate))]
internal static class OnTreasurePodOpen
{
    public static void Postfix(Il2Cpp.TreasurePod __instance)
    {
        if (HandlingPacket || string.IsNullOrEmpty(__instance._id))
            return;

        var packet = new TreasurePodPacket()
        {
            ID = __instance._id
        };

        // The other players only mark the pod as opened, they do not run its rewards. A slime
        // appearance has no packet of its own, so it travels with the pod.
        try
        {
            var slime = __instance.UnlockedSlimeAppearanceDefinition;
            var appearance = __instance.UnlockedSlimeAppearance;
            if (slime && appearance)
            {
                packet.AppearanceSlime = NetworkActorManager.GetPersistentID(slime);
                packet.AppearanceSet = appearance.SaveSet;
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
            var blueprint = __instance.Blueprint;
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