using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Drone;
using SR2MP.Components.Drone;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Drone;

[HarmonyPatch(typeof(ExplorerDroneMovementManager), nameof(ExplorerDroneMovementManager.FixedUpdate))]
internal static class ExplorerDroneMovementFreeze
{
    public static bool Prefix(ExplorerDroneMovementManager __instance)
    {
        var networkDrone = __instance.GetComponentInParent<NetworkDrone>();
        
        if (networkDrone == null && (Main.Server.IsRunning || Main.Client.IsConnected))
        {
            var explorerDrone = __instance.GetComponentInParent<ExplorerDrone>();
            if (explorerDrone)
                networkDrone = NetworkDroneManager.GetNetworkComponent(explorerDrone!.gameObject);
        }

        return networkDrone == null || networkDrone.LocallyOwned;
    }
}

[HarmonyPatch(typeof(ExplorerDroneProgram), nameof(ExplorerDroneProgram.Update))]
internal static class ExplorerDroneProgramFreeze
{
    public static bool Prefix(ExplorerDroneProgram __instance)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return true;

        var stationModel = __instance._stationModel;
        if (stationModel == null)
            return true;

        // The program is world simulation the game runs for every station, loaded or not. If every
        // game ran it, each would collect into the Quantum Cloud on its own and the income would be
        // counted once per player. One game runs it: the drone's owner, or the host when nobody
        // near it is connected. The others get its plan through ExplorerProgramStatePacket.
        try
        {
            var stationId = stationModel.actorId.Value;
            var authority = NetworkDroneManager.IsProgramAuthority(stationId);
            NetworkDroneManager.LogAuthorityChange(stationId, authority, "explorer program");
            return authority;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Explorer program authority check failed: {ex.Message}");
            return Main.Server.IsRunning;
        }
    }

    public static void Postfix(ExplorerDroneProgram __instance, bool __runOriginal)
    {
        if (!__runOriginal)
            return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        try
        {
            var stationModel = __instance._stationModel;
            if (stationModel != null)
                NetworkDroneManager.CheckProgramChanged(stationModel.actorId.Value, __instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Explorer program change check failed: {ex.Message}");
        }
    }
}
