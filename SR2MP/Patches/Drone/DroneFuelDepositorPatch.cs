using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Drone;

namespace SR2MP.Patches.Drone;

[HarmonyPatch(typeof(DroneFuelDepositor), nameof(DroneFuelDepositor.OnTriggerEnter))]
internal static class DroneFuelDepositorPatch
{
    public static bool Prefix([HarmonyArgument(0)] Collider otherCollider)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return true;

        // Every player's game sees the same fuel fly in. Only the player simulating it feeds it,
        // otherwise one piece of fuel would be counted once per player. That player's AddEnergy
        // sends the new charge to everyone, and its destroy of the item is synced as usual.
        try
        {
            if (otherCollider)
            {
                var networkActor = otherCollider.GetComponentInParent<SR2MP.Components.Actor.NetworkActor>();
                if (networkActor && !networkActor.LocallyOwned)
                    return false;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"DroneFuelDepositor prefix failed: {ex.Message}");
        }

        return true;
    }
}
