using HarmonyLib;
using SR2MP.Packets.Gadget;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Gadget;

// Blueprints were not shared at all: a player only got one from a treasure pod if that pod happened
// to be loaded on their own machine when someone else opened it.
[HarmonyPatch(typeof(GadgetDirector), nameof(GadgetDirector.AddBlueprint))]
internal static class OnBlueprintAdded
{
    public static void Postfix(GadgetDefinition __0, bool __result)
    {
        if (!__result || HandlingPacket || !__0)
            return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        try
        {
            var packet = new BlueprintsPacket();
            packet.Blueprints.Add(NetworkActorManager.GetPersistentID(__0));
            Main.SendToAllOrServer(packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share blueprint: {ex.Message}");
        }
    }
}
