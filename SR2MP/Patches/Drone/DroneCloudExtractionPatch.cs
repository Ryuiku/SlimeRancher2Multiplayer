using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Drone;
using SR2MP.Packets.Drone;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Drone;

[HarmonyPatch(typeof(DroneStation), nameof(DroneStation.AssignCloudExtractionMode))]
internal static class OnDroneCloudExtractionAssigned
{
    public static void Postfix(DroneStation __instance, bool extracting, IdentifiableType type, int count)
    {
        if (HandlingPacket) return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        var stationModel = __instance._model;
        if (stationModel == null) return;

        Main.SendToAllOrServer(new DroneCloudExtractionPacket
        {
            StationId = stationModel.actorId.Value,
            Extracting = extracting,
            TypeId = type != null ? NetworkActorManager.GetPersistentID(type) : -1,
            Count = count
        });

        // The queue itself, so every game (loaded or not) and the host's save hold the same items
        // whatever way the game fills it.
        try
        {
            NetworkDroneManager.SendExtractionQueue(stationModel);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to send cloud extraction queue: {ex.Message}");
        }
    }
}
