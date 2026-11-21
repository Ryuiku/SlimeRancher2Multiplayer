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

        // Another player just grabbed it or is carrying it. It stays theirs.
        if (!networkActor.LocallyOwned && networkActor.RecentlyClaimedByOtherPlayer)
            return;

        Claim(networkActor);
    }

    /// <summary>
    /// Makes this game the owner of the actor and tells everyone. Returns false if that is not
    /// possible right now (the host's world hasn't loaded yet).
    /// </summary>
    internal static bool Claim(NetworkActor networkActor)
    {
        if (!NetworkActorManager.CanSendActorChanges)
            return false;

        var actorId = networkActor.ActorId;
        if (actorId.Value == 0)
            return false;

        networkActor.LocallyOwned = true;
        networkActor.CurrentOwnerId = LocalID;
        networkActor.LastLocalClaimTime = UnityEngine.Time.time;
        NetworkActorManager.RememberOwner(actorId.Value, LocalID);

        Main.SendToAllOrServer(new ActorTransferPacket
        {
            ActorId = actorId,
            OwnerId = LocalID
        });

        return true;
    }
}

// The vac must never take in (or hold) something this game does not own: the tank would be filled
// here while the actor lives on for everyone else, which duplicated slimes. So own it first, and
// leave it alone if another player just grabbed it.
[HarmonyPatch(typeof(VacuumItem), nameof(VacuumItem.ConsumeVacuumable))]
internal static class OnVacConsume
{
    public static bool Prefix([HarmonyArgument(0)] GameObject gameObject)
    {
        try
        {
            if (!gameObject || (!Main.Server.IsRunning && !Main.Client.IsConnected))
                return true;

            var networkActor = gameObject.GetComponentInParent<NetworkActor>();
            if (!networkActor || networkActor.LocallyOwned)
                return true;

            if (networkActor.RecentlyClaimedByOtherPlayer)
                return false;

            // Still loading the host's world: nothing may be taken yet.
            if (!NetworkActorManager.CanSendActorChanges)
                return false;

            if (networkActor.ActorId.Value == 0)
                return true;

            return OnActorVacced.Claim(networkActor);
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"OnVacConsume failed: {exception.Message}");
            return true;
        }
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
