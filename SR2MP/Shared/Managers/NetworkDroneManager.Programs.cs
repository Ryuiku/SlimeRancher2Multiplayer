using System.Collections;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Drone;
using SR2MP.Components.Drone;
using SR2MP.Packets.Drone;

namespace SR2MP.Shared.Managers;

// Explorer drone programs and Quantum Cloud extraction. Both are world simulation that the game
// runs for every station, loaded or not, so every player's game used to collect (or eject) on its
// own and the cloud got the income once per player. Now one game runs each station: the player
// whose game has the drone awake and owns it, otherwise the host.
internal static partial class NetworkDroneManager
{
    // Last program state sent per station, so a plan that does not change is not sent every frame.
    internal static readonly Dictionary<long, (byte State, int Type, double Time, double Gather)> LastSentProgramState = new();

    // Program states for stations whose program does not exist here yet (model not initialised).
    internal static readonly Dictionary<long, ExplorerProgramStatePacket> PendingProgramStates = new();

    // Stations with a program state send waiting for the end of the frame.
    private static readonly HashSet<long> ScheduledProgramSends = new();

    // Last extraction queue size seen per station, to send the queue only when it changes.
    internal static readonly Dictionary<long, int> LastExtractionQueueSize = new();

    // Last authority answer per station, only used to log when it changes.
    private static readonly Dictionary<long, bool> LoggedAuthority = new();

    internal static void ClearPrograms()
    {
        LastSentProgramState.Clear();
        PendingProgramStates.Clear();
        ScheduledProgramSends.Clear();
        LastExtractionQueueSize.Clear();
        LoggedAuthority.Clear();
    }

    /// <summary>
    /// Whether this game runs the model-level simulation of a station (explorer program, cloud
    /// extraction). The player whose game has the drone awake and owns it runs it. When no
    /// connected player has it, the host runs it, so a station nobody is near still works once.
    /// </summary>
    internal static bool IsProgramAuthority(long stationId)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return true;

        if (NetworkDrone.Drones.TryGetValue(stationId, out var drone) && drone && !drone.IsHibernated)
            return drone.LocallyOwned;

        // A client never runs a station it does not have awake and owned.
        if (!Main.Server.IsRunning)
            return false;

        // Host with the drone not awake here: run it unless another connected player owns it,
        // because then it is awake and running on their game.
        return !IsOwnedByOtherPlayer(stationId);
    }

    /// <summary>
    /// True when another connected player is the last known owner of this station's drone.
    /// </summary>
    internal static bool IsOwnedByOtherPlayer(long stationId)
    {
        return CachedOwners.TryGetValue(stationId, out var owner) &&
               !string.IsNullOrEmpty(owner) &&
               owner != LocalID &&
               PlayerManager.CheckPlayerExists(owner);
    }

    internal static void LogAuthorityChange(long stationId, bool authority, string what)
    {
        if (!DevMode)
            return;

        if (LoggedAuthority.TryGetValue(stationId, out var last) && last == authority)
            return;

        LoggedAuthority[stationId] = authority;
        SrLogger.LogDebug($"Drone station {stationId}: {what} {(authority ? "now runs on this game" : "now runs on another game")} (owner: '{CachedOwners.GetValueOrDefault(stationId, string.Empty)}')");
    }

    #region Explorer program state

    internal static ExplorerProgramStatePacket CreateProgramState(long stationId, ExplorerDroneProgram program)
    {
        var type = program._nextResourceType;

        return new ExplorerProgramStatePacket
        {
            StationId = stationId,
            State = (byte)program._State_k__BackingField,
            NextResourceType = type != null ? NetworkActorManager.GetPersistentID(type) : -1,
            NextResourceTime = program._nextResourceTime,
            NextGatherDuration = program._NextGatherDuration_k__BackingField
        };
    }

    /// <summary>
    /// Called after the program changed its state or plan. The send waits two frames: a plan made
    /// because of a task packet must reach the others after that task packet is relayed, or their
    /// own task change would replace the plan with a new random one.
    /// </summary>
    internal static void OnProgramChanged(ExplorerDroneProgram? program)
    {
        try
        {
            if (program == null)
                return;

            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                return;

            if (!NetworkActorManager.CanSendActorChanges)
                return;

            var stationModel = program._stationModel;
            if (stationModel == null)
                return;

            var stationId = stationModel.actorId.Value;
            if (!IsProgramAuthority(stationId))
                return;

            if (ScheduledProgramSends.Contains(stationId))
                return;

            // Nothing new since the last send (the plan may be checked every frame).
            var state = CreateProgramState(stationId, program);
            if (LastSentProgramState.TryGetValue(stationId, out var last) &&
                last == (state.State, state.NextResourceType, state.NextResourceTime, state.NextGatherDuration))
                return;

            ScheduledProgramSends.Add(stationId);
            StartCoroutine(SendProgramStateLater(stationId, program));
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to queue explorer program state: {ex.Message}");
        }
    }

    /// <summary>
    /// Called every frame after the program ran here, in case it changed its plan without going
    /// through the methods patched for this. Only cheap field reads unless something changed.
    /// </summary>
    internal static void CheckProgramChanged(long stationId, ExplorerDroneProgram program)
    {
        if (LastSentProgramState.TryGetValue(stationId, out var last) &&
            last.Time == program._nextResourceTime &&
            last.Gather == program._NextGatherDuration_k__BackingField &&
            last.State == (byte)program._State_k__BackingField)
            return;

        OnProgramChanged(program);
    }

    private static IEnumerator SendProgramStateLater(long stationId, ExplorerDroneProgram program)
    {
        yield return new WaitFrames(2);

        ScheduledProgramSends.Remove(stationId);

        try
        {
            if (!NetworkActorManager.CanSendActorChanges || !IsProgramAuthority(stationId))
                yield break;

            SendProgramState(stationId, program);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Failed to send explorer program state for station {stationId}: {ex.Message}");
        }
    }

    internal static void SendProgramState(long stationId, ExplorerDroneProgram program)
    {
        var packet = CreateProgramState(stationId, program);
        var key = (packet.State, packet.NextResourceType, packet.NextResourceTime, packet.NextGatherDuration);

        if (LastSentProgramState.TryGetValue(stationId, out var last) && last == key)
            return;

        LastSentProgramState[stationId] = key;
        Main.SendToAllOrServer(packet);

        SrLogger.LogDebug($"Explorer program {stationId}: sent state {packet.State}, next type {packet.NextResourceType} at {packet.NextResourceTime:F0}");
    }

    /// <summary>
    /// Writes another game's program state into the fields. SetState or Push are not used: their
    /// bodies are unknown and could plan again or fire drone callbacks.
    /// </summary>
    internal static void ApplyProgramState(ExplorerDroneProgram program, ExplorerProgramStatePacket packet)
    {
        IdentifiableType? type = null;
        if (packet.NextResourceType != -1)
            ActorManager.ActorTypes.TryGetValue(packet.NextResourceType, out type);

        // Restored afterwards, not cleared: this also runs while the join snapshot is applied.
        var wasHandling = HandlingPacket;
        HandlingPacket = true;

        try
        {
            program._nextResourceTime = packet.NextResourceTime;
            program._nextResourceType = type;
            program._NextGatherDuration_k__BackingField = packet.NextGatherDuration;
            program._State_k__BackingField = (IExplorerDroneInterface.ExplorerDroneState)packet.State;
        }
        finally
        {
            HandlingPacket = wasHandling;
        }

        // If this game becomes the authority later it must not think it already sent this state.
        LastSentProgramState[packet.StationId] = (packet.State, packet.NextResourceType, packet.NextResourceTime, packet.NextGatherDuration);
    }

    #endregion

    #region Cloud extraction queue

    internal static List<int> GetExtractionQueue(DroneStationGadgetModel? stationModel)
    {
        var result = new List<int>();

        var queue = stationModel?._cloudPendingExtractionQueue;
        if (queue == null)
            return result;

        foreach (var type in queue.ToArray())
            result.Add(type != null ? NetworkActorManager.GetPersistentID(type) : -1);

        return result;
    }

    internal static int GetExtractionQueueSize(DroneStationGadgetModel? stationModel)
    {
        var queue = stationModel?._cloudPendingExtractionQueue;
        return queue?.Count ?? 0;
    }

    internal static void SendExtractionQueue(DroneStationGadgetModel stationModel)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        if (!NetworkActorManager.CanSendActorChanges)
            return;

        var stationId = stationModel.actorId.Value;
        var types = GetExtractionQueue(stationModel);
        LastExtractionQueueSize[stationId] = types.Count;

        Main.SendToAllOrServer(new DroneExtractionQueuePacket
        {
            StationId = stationId,
            TypeIds = types
        });

        SrLogger.LogDebug($"Drone station {stationId}: sent cloud extraction queue ({types.Count} left)");
    }

    /// <summary>
    /// Replaces the station's pending extraction queue with another game's copy. Works whether the
    /// station is loaded here or not, since the queue lives on the model (and in the save).
    /// </summary>
    internal static void ApplyExtractionQueue(DroneStationGadgetModel stationModel, List<int> typeIds)
    {
        var queue = stationModel._cloudPendingExtractionQueue;
        if (queue == null)
        {
            if (typeIds.Count == 0)
                return;

            queue = new Il2CppSystem.Collections.Generic.Queue<IdentifiableType>();
            stationModel._cloudPendingExtractionQueue = queue;
        }

        var before = queue.Count;

        // Restored afterwards, not cleared: this also runs while the join snapshot is applied.
        var wasHandling = HandlingPacket;
        HandlingPacket = true;

        try
        {
            queue.Clear();

            foreach (var typeId in typeIds)
            {
                if (typeId != -1 && ActorManager.ActorTypes.TryGetValue(typeId, out var type) && type != null)
                    queue.Enqueue(type);
            }
        }
        finally
        {
            HandlingPacket = wasHandling;
        }

        LastExtractionQueueSize[stationModel.actorId.Value] = queue.Count;
        SrLogger.LogDebug($"Drone station {stationModel.actorId.Value}: cloud extraction queue {before} -> {queue.Count}");
    }

    #endregion
}
