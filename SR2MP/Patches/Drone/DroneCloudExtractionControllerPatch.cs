using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Drone;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Drone;

// The controller ejects the items of a Quantum Cloud extraction from the station's queue. Every
// game with the station loaded ejected its own copy of each item, and each copy was synced as an
// actor spawn, so N players got N times the items. One game ejects (the drone's owner, or the host
// when no connected player owns it) and sends the queue to the others whenever it shrinks.
[HarmonyPatch(typeof(DroneStationCloudExtractionController), nameof(DroneStationCloudExtractionController.Update))]
internal static class DroneCloudExtractionControllerPatch
{
    public static bool Prefix(DroneStationCloudExtractionController __instance)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return true;

        try
        {
            var model = __instance._model;
            if (model == null)
                return true;

            var stationId = model.actorId.Value;
            var authority = NetworkDroneManager.IsProgramAuthority(stationId);

            if (NetworkDroneManager.GetExtractionQueueSize(model) > 0)
                NetworkDroneManager.LogAuthorityChange(stationId, authority, "cloud extraction");

            return authority;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Cloud extraction authority check failed: {ex.Message}");
            return Main.Server.IsRunning;
        }
    }

    public static void Postfix(DroneStationCloudExtractionController __instance, bool __runOriginal)
    {
        if (!__runOriginal)
            return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        try
        {
            var model = __instance._model;
            if (model == null)
                return;

            var stationId = model.actorId.Value;
            var size = NetworkDroneManager.GetExtractionQueueSize(model);

            if (!NetworkDroneManager.LastExtractionQueueSize.TryGetValue(stationId, out var last))
            {
                NetworkDroneManager.LastExtractionQueueSize[stationId] = size;
                return;
            }

            if (last == size)
                return;

            NetworkDroneManager.LastExtractionQueueSize[stationId] = size;
            SrLogger.LogDebug($"Drone station {stationId}: cloud extraction queue {last} -> {size} on this game");
            NetworkDroneManager.SendExtractionQueue(model);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to send cloud extraction queue: {ex.Message}");
        }
    }
}
