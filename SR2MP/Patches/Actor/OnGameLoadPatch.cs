using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Components.Actor;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Actor;

[HarmonyPatch(typeof(SceneContext), nameof(SceneContext.Start))]
internal static class OnGameLoadPatch
{
    private static bool subscribed;

    public static void Postfix()
    {
        // Once: this runs for every save that is loaded, and each time added another copy.
        if (subscribed)
            return;

        subscribed = true;
        Main.Server.OnServerStarted += OnServerStarted;
    }

    private static void OnServerStarted()
    {
        // The host's own map: zones whose areas are revealed but were never unlocked here.
        // First, so an exception in the actor loop below cannot skip it.
        SR2MP.Shared.Utils.MapEvents.RepairZonesFromRevealedAreas();

        foreach (var actor in GameState.identifiables)
        {
            if (actor.value.TryCast<ActorModel>() == null) continue;

            // The player is not a synced actor. As one, it could be frozen in place like any
            // actor another player owns.
            if (actor.value.ident != null && actor.value.ident.IsPlayer) continue;

            // Drones dont get NetworkActor
            if (NetworkDroneManager.IsDroneModel(actor.value)) continue;

            var transform = actor.value.Transform;

            if (!transform)
                continue;
            
            var networkComponent = transform.GetComponent<NetworkActor>();

            if (networkComponent)
            {
                networkComponent.CurrentOwnerId = LocalID;
                continue;
            }
            
            var networkComponent2 = transform.gameObject.AddComponent<NetworkActor>();
            networkComponent2.LocallyOwned = true;
            networkComponent2.CurrentOwnerId = LocalID;

            ActorManager.Actors[actor.value.actorId.Value] = actor.value;
        }

        // Radiant looks noted in an earlier session of this world, for slimes whose zone was not
        // loaded then and is now: put them on, so the host shows what joining players are told.
        try
        {
            foreach (var pending in NetworkActorManager.PendingRadiancy.ToArray())
            {
                if (!GameState.TryGetIdentifiableModel(new ActorId(pending.Key), out var identModel) || identModel == null)
                    continue;

                var slime = identModel.TryCast<SlimeModel>();
                if (slime == null)
                {
                    NetworkActorManager.PendingRadiancy.Remove(pending.Key);
                    continue;
                }

                if (NetworkActorManager.ApplyRadiancy(slime, pending.Value))
                    NetworkActorManager.PendingRadiancy.Remove(pending.Key);
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not restore radiant slimes: {ex.Message}");
        }

        GameState._actorIdProvider._nextActorId =
            NetworkActorManager.GetHighestActorIdInRange(0, ActorIdOffset) + 10;
    }
}