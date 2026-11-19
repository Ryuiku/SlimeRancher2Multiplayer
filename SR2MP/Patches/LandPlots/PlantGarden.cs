using HarmonyLib;
using SR2MP.Components.Actor;
using SR2MP.Components.LandPlots;
using SR2MP.Packets.LandPlots;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.LandPlots;

[HarmonyPatch(typeof(GardenCatcher), nameof(GardenCatcher.Plant))]
internal static class PlantGarden
{
    public static void Postfix(GardenCatcher __instance, IdentifiableType cropId, GameObject __result)
    {
        if (HandlingPacket)
            return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        var location = __instance ? __instance.GetComponentInParent<LandPlotLocation>() : null;
        if (!location || string.IsNullOrEmpty(location!._id))
            return;

        var packet = new GardenPlantPacket
        {
            ActorType = NetworkActorManager.GetPersistentID(cropId),
            ID = location._id
        };

        Main.SendToAllOrServer(packet);

        if (__result)
            __result.GetComponent<NetworkGarden>()?.ClaimOnReady();
    }
}

// Every player's game sees the same food fly into the garden. Only the player simulating that food
// plants it; the others get the plant from them. Other players' copies used to plant too, each
// sending its own plant and claiming the new crop, and could plant into a plot the real food missed.
[HarmonyPatch(typeof(GardenCatcher), nameof(GardenCatcher.OnTriggerEnter))]
internal static class GardenCatcherOwnerOnly
{
    public static bool Prefix([HarmonyArgument(0)] Collider col)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                return true;

            if (!col)
                return true;

            var networkActor = col.GetComponentInParent<NetworkActor>();
            if (networkActor && !networkActor.LocallyOwned)
                return false;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"GardenCatcherOwnerOnly prefix failed: {ex.Message}");
        }

        return true;
    }
}
