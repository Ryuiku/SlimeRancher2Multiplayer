using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Drone;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Drone;

// The game that runs an explorer program tells the others what it plans to collect next and when.
// Without this every game planned its own random next resource, and a game that took over a
// program after a while saw a collection time in the past and collected at once.
// HandlingPacket is not checked on purpose: a task change arrives as a packet, and the game that
// runs the program must still send the plan it makes because of it. Games that only receive the
// state write the fields directly and never call these methods, so nothing echoes back.
[HarmonyPatch(typeof(ExplorerDroneProgram))]
internal static class ExplorerProgramStatePatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(ExplorerDroneProgram.SetState))]
    public static void SetStatePostfix(ExplorerDroneProgram __instance)
        => NetworkDroneManager.OnProgramChanged(__instance);

    [HarmonyPostfix, HarmonyPatch(nameof(ExplorerDroneProgram.PlanNextResource))]
    public static void PlanNextResourcePostfix(ExplorerDroneProgram __instance)
        => NetworkDroneManager.OnProgramChanged(__instance);

    [HarmonyPostfix, HarmonyPatch(nameof(ExplorerDroneProgram.TaskUpdated))]
    public static void TaskUpdatedPostfix(ExplorerDroneProgram __instance)
        => NetworkDroneManager.OnProgramChanged(__instance);
}

// A program state that arrived before this game made the station's program is written as soon as
// the program exists.
[HarmonyPatch(typeof(DroneStationGadgetModel), nameof(DroneStationGadgetModel.InitializeForExplorer))]
internal static class OnDroneStationInitializeForExplorer
{
    public static void Postfix(DroneStationGadgetModel __instance)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                return;

            var stationId = __instance.actorId.Value;
            if (!NetworkDroneManager.PendingProgramStates.TryGetValue(stationId, out var pending))
                return;

            var program = __instance.Program;
            if (program == null)
                return;

            NetworkDroneManager.PendingProgramStates.Remove(stationId);
            NetworkDroneManager.ApplyProgramState(program, pending);
            SrLogger.LogDebug($"Explorer program {stationId}: applied the state that arrived before it existed");
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to apply a pending explorer program state: {ex.Message}");
        }
    }
}
