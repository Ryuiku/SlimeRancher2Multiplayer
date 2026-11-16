using HarmonyLib;
using SR2MP.Components.Actor;
using SR2MP.Shared.Managers;
using SR2MP.Packets.Actor;

namespace SR2MP.Patches.Actor;

[HarmonyPatch(typeof(Destroyer), nameof(Destroyer.DestroyActor), typeof(GameObject), typeof(string), typeof(bool))]
internal static class OnActorDestroy
{
    public static bool Prefix(GameObject actorObj, string source)
    {
        if (SystemContext.Instance.SceneLoader.IsSceneLoadInProgress) return true;

        try
        {
            if (Main.Server.IsRunning || Main.Client.IsConnected)
            {
                if (source is "SlimeFeral.Awake")
                {
                    return false;
                }
            }
        }
        catch
        {
            // ignored
        }

        if (HandlingPacket || !actorObj)
            return true;

        if (source is "SR2MP.ActorDestroyHandler" or "SR2MP.InitialActors" or "SR2MP.RemoveExistingGadgetModel" or "SR2MP.ReplaceDrone")
            return true;

        var actor = actorObj.GetComponent<IdentifiableActor>();
        if (!actor)
            return true;

        // Never announce the player being destroyed: every game uses the same id for its own player.
        if (actor.identType != null && actor.identType.IsPlayer)
            return true;
        
        // Prevents clients that dont own it from destroying it yia 'ResourceCycle.RegistryUpdate#1'
        var netActor = actorObj.GetComponent<NetworkActor>();
        if (netActor != null && netActor.isResource && !netActor.LocallyOwned)
        {
            if (!netActor.IsRotten)
                return false;

            ActorManager.Actors.Remove(actor.GetActorId().Value);
            return true;
        }

        // Only the player simulating a slime may destroy it. Every player's game runs slime logic
        // (eating, turning into largos, despawning, falling out of the world), so without this any
        // player whose copy of a slime got out of sync could delete it for everyone, including the host's save.
        if (netActor != null && netActor.isSlime && !netActor.LocallyOwned
            && (Main.Server.IsRunning || Main.Client.IsConnected))
        {
            SrLogger.LogPacketSize($"Blocked local destroy of slime {actor.GetActorId().Value} owned by '{netActor.CurrentOwnerId}' (source: {source})");
            return false;
        }

        // Drone destructions are handled differently
        if (actorObj.GetComponent<Il2CppMonomiPark.SlimeRancher.Drone.RanchDrone>() ||
            actorObj.GetComponent<Il2CppMonomiPark.SlimeRancher.Drone.ExplorerDrone>())
            return true;

        ActorManager.Actors.Remove(actor.GetActorId().Value);
        NetworkActorManager.KnownOwners.Remove(actor.GetActorId().Value);

        if (!NetworkActorManager.CanSendActorChanges)
            return true;

        try
        {
            var packet = new ActorDestroyPacket { ActorId = actor.GetActorId() };
            Main.SendToAllOrServer(packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Failed to send ActorDestroy packet: {ex.Message}");
        }

        return true;
    }
}