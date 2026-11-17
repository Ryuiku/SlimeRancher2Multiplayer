using System.Collections;
using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Actor;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Actor;

[PacketHandler((byte)PacketType.ActorDestroy)]
internal sealed class ActorDestroyHandler : BasePacketHandler<ActorDestroyPacket>
{
    protected override bool Handle(ActorDestroyPacket packet, IPEndPoint? _)
    {
        // Every game gives its own player the same actor id. A destroy for it must never be
        // applied or passed on: each receiver would delete its own player.
        if (NetworkActorManager.IsPlayerActor(packet.ActorId))
        {
            SrLogger.LogWarning("Ignored a destroy packet that targets the player.");
            return false;
        }

        NetworkActorManager.KnownOwners.Remove(packet.ActorId.Value);

        // While this game is loading a zone the destroy is applied afterwards. It is still passed
        // on to the other players right away.
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
            Apply(packet);

        return true;
    }

    private static void Apply(ActorDestroyPacket packet)
    {
        // Again here: a spawn that waited in the queue ahead of this destroy put them back.
        NetworkActorManager.KnownOwners.Remove(packet.ActorId.Value);
        NetworkActorManager.PendingRadiancy.Remove(packet.ActorId.Value);

        if (!GameState.TryGetIdentifiableModel(packet.ActorId, out var actor))
        {
            ActorManager.Actors.Remove(packet.ActorId.Value);
            return;
        }

        HandlingPacket = true;
        try
        {
            // Take the object before the model goes away, the model is how it is found.
            GameObject? obj = null;
            try { obj = actor.GetGameObject(); } catch { /* ignored */ }

            // Remove the drone station if it's a drone
            if (actor.TryCast<DroneStationGadgetModel>() != null)
                NetworkDroneManager.RemoveStationDrone(packet.ActorId);

            RemoveStatueSlime(actor);

            NetworkGadgetManager.RemoveTeleporterGadget(actor);
            NetworkGadgetManager.ForgetGadgetLink(packet.ActorId);

            GameState.identifiables.Remove(packet.ActorId);
            if (actor.ident != null && GameState.identifiablesByIdent.TryGetValue(actor.ident, out var sameType))
                sameType.Remove(actor);
            GameState.DestroyIdentifiableModel(actor);
            ActorManager.Actors.Remove(packet.ActorId.Value);

            StartCoroutine(DestroyActor(actor, obj));
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    /// <summary>
    /// The game that turned a ringtail to stone keeps the slime's model while the statue exists, so
    /// it can bring the slime back. When another player removes the statue (they turned it back
    /// into a ringtail of their own), that kept model would otherwise stay in this save forever and
    /// come back as a second ringtail on the next zone load.
    /// </summary>
    private static void RemoveStatueSlime(IdentifiableModel actor)
    {
        try
        {
            var statue = actor.TryCast<Il2CppMonomiPark.SlimeRancher.DataModel.StatueFormModel>();
            if (statue == null)
                return;

            var slimeId = statue.referencedActorId;
            if (slimeId.Value == 0 || !GameState.TryGetIdentifiableModel(slimeId, out var slime) || slime == null)
                return;

            GameState.identifiables.Remove(slimeId);
            if (slime.ident != null && GameState.identifiablesByIdent.TryGetValue(slime.ident, out var sameType))
                sameType.Remove(slime);
            GameState.DestroyIdentifiableModel(slime);
            ActorManager.Actors.Remove(slimeId.Value);
            NetworkActorManager.KnownOwners.Remove(slimeId.Value);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not remove the slime kept for a statue: {ex.Message}");
        }
    }

    private static IEnumerator DestroyActor(IdentifiableModel actor, GameObject? known)
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                var obj = known ? known : actor.GetGameObject();

                if (obj)
                {
                    Destroyer.DestroyAny(obj, "SR2MP.ActorDestroyHandler");
                    yield break;
                }
            }
            catch
            {
                // ignored
            }

            yield return null; // wait one frame
        }
    }
}