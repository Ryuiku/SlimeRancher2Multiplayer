using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using SR2MP.Components.Actor;
using SR2MP.Packets.Actor;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Actor;

[HarmonyPatch(typeof(Vacuumable), nameof(Vacuumable.Capture))]
internal static class OnActorVacced
{
    // Prefix: ownership has to move to the player vacuuming the actor before Capture runs,
    // since capturing destroys the actor, and only the owner may destroy slimes.
    public static void Prefix(Vacuumable __instance)
    {
        var networkActor = __instance.GetComponent<NetworkActor>();
        if (!networkActor)
            return;

        Claim(networkActor);
    }

    internal static void Claim(NetworkActor networkActor)
    {
        if (!NetworkActorManager.CanSendActorChanges)
            return;

        var actorId = networkActor.ActorId;
        if (actorId.Value == 0)
            return;

        networkActor.LocallyOwned = true;
        networkActor.CurrentOwnerId = LocalID;

        Main.SendToAllOrServer(new ActorTransferPacket
        {
            ActorId = actorId,
            OwnerId = LocalID
        });
    }
}

// Items in the vac cone are pulled by physics, which only works for the owner (everyone else has the
// rigidbody frozen). Without this, items another player owns (statues, toys, big items) can't be
// picked up at all. Ownership moves as soon as the vac starts pulling, unless another player just
// grabbed or is holding the item.
[HarmonyPatch(typeof(VacuumItem), nameof(VacuumItem.Consume))]
internal static class OnVacPull
{
    public static void Prefix([HarmonyArgument(0)] Il2CppSystem.Collections.Generic.HashSet<GameObject> inVac)
    {
        if (inVac == null || !NetworkActorManager.CanSendActorChanges)
            return;

        try
        {
            foreach (var gameObject in inVac)
            {
                if (!gameObject)
                    continue;

                var networkActor = gameObject.GetComponentInParent<NetworkActor>();
                if (!networkActor || networkActor.LocallyOwned || networkActor.RecentlyClaimedByOtherPlayer)
                    continue;

                OnActorVacced.Claim(networkActor);
            }
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"OnVacPull failed: {exception.Message}");
        }
    }
}

// While holding an item, keep telling everyone we have it so nobody pulls it out of our vac.
[HarmonyPatch(typeof(VacuumItem), nameof(VacuumItem.LateUpdate))]
internal static class OnVacHold
{
    private static float nextClaim;

    public static void Postfix(VacuumItem __instance)
    {
        var held = __instance._held;
        if (!held || UnityEngine.Time.time < nextClaim)
            return;

        nextClaim = UnityEngine.Time.time + 1f;

        var networkActor = held.GetComponent<NetworkActor>();
        if (networkActor)
            OnActorVacced.Claim(networkActor);
    }
}
