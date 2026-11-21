using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Regions;
using SR2MP.Packets.World;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Ranch catch-up. Every game remembers on its own when its copy of a ranch area went to sleep
/// and, when the area wakes, feeds, collects and grows for all the time since. With two players
/// that covered the same hours twice: the time one player spent at the ranch was simulated live
/// there and then again by the other player's game when they came back.
///
/// Here every player reports how far the ranch areas awake on their game have been simulated
/// (world time, every few seconds). Other games move their own "asleep since" time of that area
/// forward to it, so they only catch up the time nobody simulated. The host also picks one player
/// per area (itself when it is there) whose game runs the catch-up after a sleep or when the area
/// wakes, and the auto-feeders of that area; the other games skip those parts.
/// A client that wakes an area before it knows who simulates it holds that catch-up and asks the
/// host, which answers at once; the host never waits, since it is always picked when it is there.
/// </summary>
internal static class NetworkRanchCellManager
{
    private const float HeartbeatInterval = 5f;

    // A simulator not confirmed for this long is treated as unknown (gone, or left the area).
    private const float FreshFor = 15f;

    private static readonly List<RanchCellFastForwarder> Cells = new();
    private static readonly Dictionary<IntPtr, string> CellByRegion = new();

    // Host only: when each player last reported each area awake (real time), by area, then player.
    private static readonly Dictionary<string, Dictionary<string, float>> LastAwake = new();

    // Who runs each area's catch-up, and when that was last confirmed (real time).
    private static readonly Dictionary<string, (string PlayerId, float Since)> Simulators = new();

    // The furthest world time any player has simulated each area to.
    private static readonly Dictionary<string, double> SimulatedUntil = new();

    private static readonly HashSet<string> LoggedNoTime = new();
    private static readonly HashSet<string> LoggedSkips = new();

    private static float nextHeartbeat;
    private static float nextRegionRebuild;
    private static readonly HashSet<IntPtr> LoggedUnknownRegions = new();

    // A client's held catch-up waits this long for the host to name the area's simulator; after
    // that it runs here, since an answer that never comes would otherwise lose it for good.
    private const float PendingTimeout = 15f;

    private sealed class PendingCatchUp
    {
        public RanchCellFastForwarder Cell = null!;
        public readonly List<(string Part, Action Run)> Parts = new();
        public float Since;
    }

    // Client only: catch-up parts held while the host has not yet said who simulates the area.
    private static readonly Dictionary<string, PendingCatchUp> Pending = new();

    private static bool runningHeld;
    private static int catchUpDepth;

    /// <summary>
    /// True while this game runs a corral or pond catch-up. Its feeding has no food object and is
    /// done by the area's simulator for every slime there, whoever owns the slime right now.
    /// </summary>
    internal static bool RunningCatchUp => catchUpDepth > 0;

    internal static void BeginCatchUp() => catchUpDepth++;

    internal static void EndCatchUp()
    {
        if (catchUpDepth > 0)
            catchUpDepth--;
    }

    static NetworkRanchCellManager()
    {
        Main.Server.OnServerStarted +=      ClearSession;
        Main.Client.OnConnected     += _ => ClearSession();
        Main.Client.OnDisconnected  +=      ClearSession;
    }

    private static void ClearSession()
    {
        LastAwake.Clear();
        Simulators.Clear();
        SimulatedUntil.Clear();
        LoggedNoTime.Clear();
        LoggedSkips.Clear();
        nextHeartbeat = 0f;
    }

    private static bool InSession => Main.Server.IsRunning || Main.Client.IsConnected;

    #region Tracking

    internal static void Track(RanchCellFastForwarder cell)
    {
        if (cell == null)
            return;

        Cells.RemoveAll(c => c == null || c.Pointer == cell.Pointer);
        Cells.Add(cell);
        RebuildRegions();
    }

    internal static void Untrack(RanchCellFastForwarder cell)
    {
        if (cell == null)
            return;

        // The area is going away with its scene, maybe without going to sleep first: its last
        // awake moment is sent now so it is not lost.
        if (IsAwake(cell))
            SendFinal(cell);

        Cells.RemoveAll(c => c == null || c.Pointer == cell.Pointer);
        RebuildRegions();
    }

    private static void RebuildRegions()
    {
        CellByRegion.Clear();
        foreach (var cell in Cells)
        {
            try
            {
                var region = cell ? cell._region : null;
                var id = CellId(cell);
                if (region != null && id != null)
                    CellByRegion[region.Pointer] = id;
            }
            catch
            {
                // not set up yet; found again on the next change
            }
        }
    }

    internal static string? CellId(RanchCellFastForwarder? cell)
    {
        try
        {
            if (!cell)
                return null;

            var id = cell!.Id;
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAwake(RanchCellFastForwarder? cell)
    {
        try
        {
            return cell && cell!._region != null && !cell._region.Hibernated;
        }
        catch
        {
            return false;
        }
    }

    private static RanchCellFastForwarder? FindCell(string id)
    {
        foreach (var cell in Cells)
        {
            if (cell && CellId(cell) == id)
                return cell;
        }

        return null;
    }

    private static RanchCellModel? GetModel(string id)
    {
        try
        {
            var cells = SceneContext.Instance ? SceneContext.Instance.GameModel?.ranch?._ranchCells : null;
            if (cells == null)
                return null;

            return cells.TryGetValue(id, out var model) ? model : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Heartbeat

    /// <summary>Called every frame on the main thread.</summary>
    internal static void Tick()
    {
        // A catch-up runs within one frame; a count left over means a call ended without its
        // postfix (it threw), and must not let later feeding through.
        catchUpDepth = 0;

        var now = Time.realtimeSinceStartup;

        if (Pending.Count > 0)
        {
            foreach (var (id, pending) in Pending.ToArray())
            {
                // Held catch-up waiting for a host that is gone: nobody else will run it now.
                // Run from here, on the main thread, as the disconnect may come from another.
                if (!InSession)
                {
                    Pending.Remove(id);
                    RunHeld(id, pending, "the session ended");
                    continue;
                }

                if (now - pending.Since < PendingTimeout)
                    continue;

                Pending.Remove(id);
                RunHeld(id, pending, "the host did not name a simulator in time");
            }
        }

        if (!InSession || now < nextHeartbeat)
            return;
        nextHeartbeat = now + HeartbeatInterval;

        if (!NetworkActorManager.CanSendActorChanges)
            return;

        var timeDirector = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        if (!timeDirector)
            return;

        // During a sleep the clock jumps ahead before the catch-up has run, so the time is not
        // reported then; the heartbeat still goes out so everyone keeps knowing who simulates.
        var fastForwarding = timeDirector.IsFastForwarding();
        var worldTime = timeDirector.WorldTime();

        var entries = new List<RanchCellSyncPacket.Entry>();
        var awakeHere = new HashSet<string>();

        foreach (var cell in Cells.ToArray())
        {
            if (!IsAwake(cell))
                continue;

            var id = CellId(cell);
            if (id == null || !awakeHere.Add(id))
                continue;

            if (!fastForwarding)
                Raise(SimulatedUntil, id, worldTime);

            if (Main.Server.IsRunning)
                MarkAwake(id, LocalID, now);
            else
                entries.Add(NewEntry(id, string.Empty));
        }

        if (Main.Server.IsRunning)
        {
            PickSimulators(now);

            foreach (var (id, simulator) in Simulators)
                entries.Add(NewEntry(id, simulator.PlayerId));

            // Areas awake here that somehow have no simulator still carry the time.
            foreach (var id in awakeHere)
            {
                if (!Simulators.ContainsKey(id))
                    entries.Add(NewEntry(id, string.Empty));
            }
        }

        if (entries.Count == 0)
            return;

        Main.SendToAllOrServer(new RanchCellSyncPacket { Entries = entries });
    }

    private static RanchCellSyncPacket.Entry NewEntry(string id, string simulatorId) => new()
    {
        CellId = id,
        SimulatedUntil = SimulatedUntil.TryGetValue(id, out var until) ? until : 0d,
        SimulatorId = simulatorId
    };

    private static void SendFinal(RanchCellFastForwarder cell)
    {
        try
        {
            if (!InSession || !NetworkActorManager.CanSendActorChanges)
                return;

            var id = CellId(cell);
            var timeDirector = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
            if (id == null || !timeDirector || timeDirector.IsFastForwarding())
                return;

            Raise(SimulatedUntil, id, timeDirector.WorldTime());

            var simulatorId = string.Empty;
            if (Main.Server.IsRunning && Simulators.TryGetValue(id, out var simulator))
                simulatorId = simulator.PlayerId;

            Main.SendToAllOrServer(new RanchCellSyncPacket
            {
                Entries = new List<RanchCellSyncPacket.Entry> { NewEntry(id, simulatorId) }
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not send a ranch area's last awake time: {ex.Message}");
        }
    }

    private static void MarkAwake(string id, string playerId, float now)
    {
        if (string.IsNullOrEmpty(playerId))
            return;

        if (!LastAwake.TryGetValue(id, out var players))
            LastAwake[id] = players = new Dictionary<string, float>();

        players[playerId] = now;
    }

    // Host: the host itself when its game has the area awake, else the player already simulating
    // it if still there, else the player with the lowest id among those who have it awake.
    private static void PickSimulators(float now)
    {
        foreach (var (id, players) in LastAwake)
        {
            string? chosen = null;

            if (players.TryGetValue(LocalID, out var hostSeen) && now - hostSeen <= FreshFor)
            {
                chosen = LocalID;
            }
            else
            {
                if (Simulators.TryGetValue(id, out var current)
                    && players.TryGetValue(current.PlayerId, out var currentSeen)
                    && now - currentSeen <= FreshFor
                    && PlayerManager.CheckPlayerExists(current.PlayerId))
                {
                    chosen = current.PlayerId;
                }
                else
                {
                    foreach (var (playerId, seen) in players)
                    {
                        if (now - seen > FreshFor || playerId == LocalID || !PlayerManager.CheckPlayerExists(playerId))
                            continue;

                        if (chosen == null || string.CompareOrdinal(playerId, chosen) < 0)
                            chosen = playerId;
                    }
                }
            }

            if (chosen == null)
            {
                Simulators.Remove(id);
                continue;
            }

            if (!Simulators.TryGetValue(id, out var previous) || previous.PlayerId != chosen)
                SrLogger.LogDebug($"Ranch area {id} is now simulated by {chosen}");

            Simulators[id] = (chosen, now);
        }
    }

    #endregion

    #region Receiving

    internal static void Apply(RanchCellSyncPacket packet, string? senderId)
    {
        var now = Time.realtimeSinceStartup;

        foreach (var entry in packet.Entries)
        {
            if (string.IsNullOrEmpty(entry.CellId))
                continue;

            try
            {
                if (Main.Server.IsRunning)
                {
                    // Only the host decides who simulates; a player's report means "awake here".
                    if (!string.IsNullOrEmpty(senderId))
                    {
                        MarkAwake(entry.CellId, senderId!, now);

                        // A player just woke an area nobody simulates: it holds its catch-up
                        // until it hears who does, so the answer goes out on the next frame.
                        if (!Simulators.TryGetValue(entry.CellId, out var current) || now - current.Since > FreshFor)
                            nextHeartbeat = 0f;
                    }
                }
                else if (!string.IsNullOrEmpty(entry.SimulatorId))
                {
                    Simulators[entry.CellId] = (entry.SimulatorId, now);
                    ResolveHeld(entry.CellId, entry.SimulatorId);
                }

                if (entry.SimulatedUntil <= 0d)
                    continue;

                if (packet.Snapshot)
                    SimulatedUntil[entry.CellId] = entry.SimulatedUntil;
                else
                    Raise(SimulatedUntil, entry.CellId, entry.SimulatedUntil);

                // An area awake here is being simulated live by this game: its own time is right.
                var cell = FindCell(entry.CellId);
                if (IsAwake(cell))
                    continue;

                var model = cell ? cell!._model : GetModel(entry.CellId);
                MoveSleepTime(entry.CellId, model, entry.SimulatedUntil, packet.Snapshot);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Could not apply ranch area time for {entry.CellId}: {ex.Message}");
            }
        }
    }

    // Moves the time the area went to sleep forward to "simulated until", so waking only catches
    // up what nobody simulated. The joining player's own save's time means nothing in the host's
    // world, so the host's snapshot replaces it. A time is never made up where the game has none.
    private static void MoveSleepTime(string id, RanchCellModel? model, double simulatedUntil, bool replace)
    {
        if (model == null)
            return;

        var current = model.hibernationTime;
        if (current == null || !current.HasValue)
        {
            if (LoggedNoTime.Add(id))
                SrLogger.LogDebug($"Ranch area {id} has no asleep-since time here; its catch-up time was left alone");
            return;
        }

        var timeDirector = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        if (timeDirector)
            simulatedUntil = Math.Min(simulatedUntil, timeDirector!.WorldTime());

        var old = current.Value;
        if (!replace && simulatedUntil <= old)
            return;
        if (replace && Math.Abs(simulatedUntil - old) < 0.001)
            return;

        model.hibernationTime = new Il2CppSystem.Nullable<double>(simulatedUntil);
        SrLogger.LogDebug($"Ranch area {id}: asleep-since time moved from {old:F0} to {simulatedUntil:F0}");
    }

    private static void Raise(Dictionary<string, double> times, string id, double value)
    {
        if (!times.TryGetValue(id, out var current) || value > current)
            times[id] = value;
    }

    #endregion

    #region Game hooks

    /// <summary>
    /// The area changes between asleep and awake. Going to sleep: its last awake moment is sent.
    /// Waking: its asleep-since time is moved to what the others simulated meanwhile, right before
    /// the game works out the catch-up from it.
    /// </summary>
    internal static void OnHibernationChanging(RanchCellFastForwarder cell, bool hibernating)
    {
        if (!InSession)
            return;

        try
        {
            if (hibernating)
            {
                SendFinal(cell);
                return;
            }

            var id = CellId(cell);
            if (id != null && SimulatedUntil.TryGetValue(id, out var until))
                MoveSleepTime(id, cell._model, until, false);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Ranch area hibernation hook failed: {ex.Message}");
        }
    }

    /// <summary>
    /// True when this game must not run that part of the area's catch-up (feeding, collecting,
    /// growing for the time away or asleep) now: another player's game runs it, or this client
    /// does not know yet who does and holds it (<paramref name="run"/> repeats the call later).
    /// Two games waking the same area together both found no simulator and both ran it, so
    /// collectors got the plorts twice and feeders were drained twice.
    /// </summary>
    internal static bool ShouldSkipCatchUp(RanchCellFastForwarder cell, string part, double from, double to, Action run)
    {
        try
        {
            if (!InSession || HandlingPacket || runningHeld)
                return false;

            var id = CellId(cell);
            if (id == null)
                return false;

            if (IsOtherSimulator(id))
            {
                var key = $"{id}:{part}:{from:F0}";
                if (LoggedSkips.Add(key))
                    SrLogger.LogDebug($"Skipped the {part} catch-up of ranch area {id} ({from:F0} to {to:F0}): {Simulators[id].PlayerId} simulates it");

                return true;
            }

            if (IsLocalSimulator(id))
                return false;

            // The host is the one picked whenever it has the area awake, so it takes the area
            // now and tells the others right away instead of waiting for its next heartbeat.
            if (Main.Server.IsRunning)
            {
                ClaimForHost(id);
                return false;
            }

            Hold(cell, id, part, from, to, run);
            return true;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Ranch catch-up check failed: {ex.Message}");
            return false;
        }
    }

    private static void ClaimForHost(string id)
    {
        var now = Time.realtimeSinceStartup;
        MarkAwake(id, LocalID, now);

        if (!Simulators.TryGetValue(id, out var previous) || previous.PlayerId != LocalID)
            SrLogger.LogDebug($"Ranch area {id} woke on the host with no simulator; the host simulates it");

        Simulators[id] = (LocalID, now);
        nextHeartbeat = 0f;
    }

    private static void Hold(RanchCellFastForwarder cell, string id, string part, double from, double to, Action run)
    {
        var now = Time.realtimeSinceStartup;
        var isNew = false;

        if (!Pending.TryGetValue(id, out var pending) || !pending.Cell || pending.Cell.Pointer != cell.Pointer)
        {
            Pending[id] = pending = new PendingCatchUp { Cell = cell, Since = now };
            isNew = true;
        }

        pending.Parts.Add((part, run));
        SrLogger.LogDebug($"Held the {part} catch-up of ranch area {id} ({from:F0} to {to:F0}) until the host says who simulates it");

        // Asks the host now; the regular report would only come with the next heartbeat.
        if (isNew && NetworkActorManager.CanSendActorChanges)
        {
            Main.SendToAllOrServer(new RanchCellSyncPacket
            {
                Entries = new List<RanchCellSyncPacket.Entry> { NewEntry(id, string.Empty) }
            });
        }
    }

    // Client: the host named the area's simulator. Held catch-up runs here only if it is us.
    private static void ResolveHeld(string id, string simulatorId)
    {
        if (!Pending.TryGetValue(id, out var pending))
            return;

        Pending.Remove(id);

        if (simulatorId == LocalID)
        {
            RunHeld(id, pending, "this game simulates it");
            return;
        }

        SrLogger.LogDebug($"Dropped the held catch-up of ranch area {id}: {simulatorId} simulates it");
    }

    private static void RunHeld(string id, PendingCatchUp pending, string reason)
    {
        if (!pending.Cell)
            return;

        SrLogger.LogDebug($"Running the held catch-up of ranch area {id}: {reason}");

        runningHeld = true;
        try
        {
            foreach (var (part, run) in pending.Parts)
            {
                try
                {
                    run();
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Held {part} catch-up of ranch area {id} failed: {ex.Message}");
                }
            }
        }
        finally
        {
            runningHeld = false;
        }
    }

    private static bool IsLocalSimulator(string id)
    {
        if (!Simulators.TryGetValue(id, out var simulator) || simulator.PlayerId != LocalID)
            return false;

        var timeDirector = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        if (timeDirector && timeDirector!.IsFastForwarding())
            return true;

        return Time.realtimeSinceStartup - simulator.Since <= FreshFor;
    }

    /// <summary>
    /// True when another player's game drives the auto-feeders of the ranch area this region
    /// belongs to. Unknown region or simulator: false (the game runs as usual). Also true while this
    /// client holds the area's catch-up waiting for the host's answer.
    /// </summary>
    internal static bool IsOtherSimulator(Region? region)
    {
        if (region == null || !InSession || (Simulators.Count == 0 && Pending.Count == 0))
            return false;

        var pointer = region.Pointer;
        if (!CellByRegion.TryGetValue(pointer, out var id))
        {
            // An area's region may be set up after it started: looked for again now and then.
            var now = Time.realtimeSinceStartup;
            if (now < nextRegionRebuild)
                return false;
            nextRegionRebuild = now + 1f;

            RebuildRegions();
            if (!CellByRegion.TryGetValue(pointer, out id))
            {
                if (LoggedUnknownRegions.Add(pointer))
                    SrLogger.LogWarning($"Auto-feeder region '{region.name}' belongs to no known ranch area; it runs on every game there");
                return false;
            }
        }

        // While this client holds the area's catch-up it does not know yet who runs the feeders. Running
        // them meanwhile would feed from the old schedule on top of the catch-up the host may be running.
        return Pending.ContainsKey(id) || IsOtherSimulator(id);
    }

    private static bool IsOtherSimulator(string id)
    {
        if (!Simulators.TryGetValue(id, out var simulator))
            return false;

        if (string.IsNullOrEmpty(simulator.PlayerId) || simulator.PlayerId == LocalID)
            return false;

        // A sleep pauses the heartbeat's times for a while; who simulates does not change then.
        var timeDirector = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        if (timeDirector && timeDirector!.IsFastForwarding())
            return true;

        return Time.realtimeSinceStartup - simulator.Since <= FreshFor;
    }

    #endregion

    #region Join

    /// <summary>
    /// For a joining player: how far the host's world has simulated each ranch area (now for the
    /// areas awake on the host, else when they went to sleep there), and who simulates them.
    /// </summary>
    internal static RanchCellSyncPacket CreateSnapshot()
    {
        var entries = new List<RanchCellSyncPacket.Entry>();

        var cells = SceneContext.Instance ? SceneContext.Instance.GameModel?.ranch?._ranchCells : null;
        var timeDirector = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        if (cells == null || !timeDirector)
            return new RanchCellSyncPacket { Snapshot = true, Entries = entries };

        var now = Time.realtimeSinceStartup;

        foreach (var pair in cells)
        {
            var id = pair.key;
            if (string.IsNullOrEmpty(id) || pair.value == null)
                continue;

            double until;
            if (IsAwake(FindCell(id)))
            {
                until = timeDirector!.WorldTime();
            }
            else
            {
                var time = pair.value.hibernationTime;
                if (time == null || !time.HasValue)
                    continue;
                until = time.Value;
            }

            var simulatorId = Simulators.TryGetValue(id, out var simulator) && now - simulator.Since <= FreshFor
                ? simulator.PlayerId
                : string.Empty;

            entries.Add(new RanchCellSyncPacket.Entry { CellId = id, SimulatedUntil = until, SimulatorId = simulatorId });
        }

        return new RanchCellSyncPacket { Snapshot = true, Entries = entries };
    }

    #endregion
}
