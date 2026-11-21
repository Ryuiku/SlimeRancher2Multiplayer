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
        // Sent even when the blueprint was not new for this player: it can still be new for the
        // others (a player who joined with a further progressed save opens a pod in the host's world).
        if (HandlingPacket || !__0)
            return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        // A joining player's game is still handing out the blueprints of their own save.
        if (!NetworkActorManager.CanSendActorChanges)
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
