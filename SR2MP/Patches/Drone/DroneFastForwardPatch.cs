using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Drone;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Drone;

// A ranch drone's catch-up (after a sleep, or when its ranch area wakes) moves items between plots.
// Every game with the area awake ran it, so the same items could be moved once per player. It is
// skipped while another connected player owns the drone, since that game runs it. The usual owner
// check is not used here: when an area wakes, its drone is often not claimed yet, and the player
// arriving there must still run the catch-up.
[HarmonyPatch(typeof(DroneFastForwarder), nameof(DroneFastForwarder.FastForward))]
internal static class DroneFastForwardPatch
{
    private static readonly HashSet<long> LoggedSkips = new();

    public static bool Prefix([HarmonyArgument(0)] RanchDrone drone)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return true;

        try
        {
            var stationModel = drone ? drone._stationModel : null;
            if (stationModel == null)
                return true;

            var stationId = stationModel.actorId.Value;
            if (!NetworkDroneManager.IsOwnedByOtherPlayer(stationId))
            {
                LoggedSkips.Remove(stationId);
                return true;
            }

            if (LoggedSkips.Add(stationId))
                SrLogger.LogDebug($"Skipped the ranch drone catch-up of station {stationId}: {NetworkDroneManager.CachedOwners[stationId]} runs that drone");

            return false;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Ranch drone catch-up check failed: {ex.Message}");
            return Main.Server.IsRunning;
        }
    }
}
