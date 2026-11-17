using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Components.LandPlots;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Loading;

namespace SR2MP.Shared.Managers;

internal static class NetworkGardenManager
{
    private static readonly Dictionary<string, NetworkGarden> Gardens = new();
    
    private static readonly Dictionary<string, string> KnownOwners = new();

    private static readonly HashSet<string> PendingClaims = new();
    private static readonly Dictionary<string, GardenUpdatePacket.Entry> PendingFinalStates = new();

    private static int lastTickFrame = -1;
    private static float nextClaimFlush;
    private static float nextStateFlush;
    private const float ClaimFlushInterval = 1f;
    private const float StateFlushInterval = 5f;

    internal static void OnServerStarted()
    {
        KnownOwners.Clear();
        PendingClaims.Clear();
        PendingFinalStates.Clear();

        foreach (var spawnResource in Object.FindObjectsOfType<SpawnResource>(true))
        {
            if (spawnResource.GetComponent<NetworkGarden>() == null)
                spawnResource.gameObject.AddComponent<NetworkGarden>();
        }

        var claimed = 0;
        foreach (var garden in Gardens.Values)
        {
            if (!garden || garden.IsHibernated)
                continue;

            garden.TakeOwnership(broadcast: false);
            claimed++;
        }

        SrLogger.LogDebug($"Host took ownership of {claimed} registered gardens on server start.");
    }

    internal static void OnDisconnected()
    {
        KnownOwners.Clear();
        PendingClaims.Clear();
        PendingFinalStates.Clear();

        foreach (var garden in Gardens.Values)
        {
            if (garden)
                garden.ResetToVanilla();
        }
    }

    internal static void Register(NetworkGarden garden)
    {
        var id = garden.Id!;
        Gardens[id] = garden;
        SrLogger.LogGarden($"Garden registered in manager: id='{id}', total={Gardens.Count}");

        if (Main.Server.IsRunning &&
            KnownOwners.TryGetValue(id, out var owner) &&
            owner != LocalID &&
            PlayerManager.CheckPlayerExists(owner))
        {
            SrLogger.LogGarden($"Garden '{id}' restoring known owner '{owner}' on register");
            garden.SetOwner(owner);
        }
    }

    internal static void Unregister(NetworkGarden garden)
    {
        if (garden.Id != null && Gardens.TryGetValue(garden.Id, out var registered) && registered == garden)
            Gardens.Remove(garden.Id);
    }

    internal static void QueueClaim(string gardenId)
        => PendingClaims.Add(gardenId);

    internal static void QueueFinalState(NetworkGarden garden)
    {
        var entry = garden.CaptureState();
        if (entry != null)
            PendingFinalStates[entry.Value.GardenId] = entry.Value;
    }

    internal static void ApplyOwnership(string gardenId, string ownerId)
    {
        if (Main.Server.IsRunning)
            KnownOwners[gardenId] = ownerId;

        if (!Gardens.TryGetValue(gardenId, out var garden) || !garden)
            return;

        // An empty owner is a release (the owner hibernated)
        // Others are free to take over
        if (string.IsNullOrEmpty(ownerId))
        {
            SrLogger.LogGarden($"Garden '{gardenId}' ownership release received");
            garden.OnOwnerReleased();
        }
        else
        {
            SrLogger.LogGarden($"Garden '{gardenId}' ownership claim received from '{ownerId}'");
            garden.SetOwner(ownerId);
        }
    }
    
    internal static void BroadcastRelease(string gardenId)
    {
        if (Main.Server.IsRunning)
            KnownOwners[gardenId] = string.Empty;

        SrLogger.LogGarden($"Broadcasting garden release: id='{gardenId}'");

        Main.SendToAllOrServer(new GardenOwnershipPacket
        {
            Entries = new List<GardenOwnershipPacket.Entry> { new() { GardenId = gardenId, OwnerId = string.Empty } }
        });
    }

    internal static void ApplyState(GardenUpdatePacket.Entry entry)
    {
        var found = Gardens.TryGetValue(entry.GardenId, out var garden) && garden;
        SrLogger.LogGarden($"Garden update received: id='{entry.GardenId}', found={found}, nextSpawnTime={entry.NextSpawnTime}, storedWater={entry.StoredWater}");
        if (found)
        {
            garden!.ApplyUpdate(entry.NextSpawnTime, entry.StoredWater, entry.NextSpawnRipens);
            return;
        }

        ApplyToModel(entry);
    }

    #region Gardens that are not loaded here

    // Growth for gardens with no saved data here yet, by garden id. Applied when the garden loads.
    private static readonly Dictionary<string, GardenUpdatePacket.Entry> PendingStates = new();

    // A joining player: the host's saved growth for spawners this save has no data for yet (it
    // only makes it when their zone first loads), by position rounded to whole metres. Written
    // when the spawner loads here, so it does not start on this save's own defaults and then
    // send them to the host as the owner's growth.
    private static readonly Dictionary<(int, int, int), List<InitialResourceSpawnersPacket.Entry>> HostSpawnersNotHere = new();

    static NetworkGardenManager()
    {
        Main.Server.OnServerStarted +=      ClearPending;
        Main.Client.OnConnected     += _ => ClearPending();
        Main.Client.OnDisconnected  +=      ClearPending;
    }

    private static void ClearPending()
    {
        PendingStates.Clear();
        HostSpawnersNotHere.Clear();
    }

    internal static bool TakePendingState(string gardenId, out GardenUpdatePacket.Entry entry)
        => PendingStates.Remove(gardenId, out entry);

    internal static bool TakeHostSpawnerState(Vector3 position, out InitialResourceSpawnersPacket.Entry entry)
    {
        entry = default;
        if (HostSpawnersNotHere.Count == 0)
            return false;

        var (x, y, z) = GridKey(position);
        List<InitialResourceSpawnersPacket.Entry>? bestBucket = null;
        var bestIndex = -1;
        var bestDistance = SamePositionSqr;

        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        for (var dz = -1; dz <= 1; dz++)
        {
            if (!HostSpawnersNotHere.TryGetValue((x + dx, y + dy, z + dz), out var bucket))
                continue;

            for (var i = 0; i < bucket.Count; i++)
            {
                var distance = (bucket[i].Position - position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestBucket = bucket;
                bestIndex = i;
            }
        }

        if (bestBucket == null)
            return false;

        entry = bestBucket[bestIndex];
        bestBucket.RemoveAt(bestIndex);
        if (bestBucket.Count == 0)
            HostSpawnersNotHere.Remove(GridKey(entry.Position));
        return true;
    }

    // The garden is not loaded here: its saved data (which the game finds by position when the
    // area loads) gets the owner's growth, so this save, the host's included, keeps it.
    private static void ApplyToModel(GardenUpdatePacket.Entry entry)
    {
        try
        {
            var model = entry.Position == Vector3.zero ? null : FindSpawnerModel(entry.Position);
            if (model == null)
            {
                PendingStates[entry.GardenId] = entry;
                return;
            }

            model.nextSpawnTime = entry.NextSpawnTime;
            model.storedWater = entry.StoredWater;
            model.nextSpawnRipens = entry.NextSpawnRipens;
            PendingStates.Remove(entry.GardenId);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not write garden '{entry.GardenId}' to the save: {ex.Message}");
        }
    }

    // Saved spawner data by position, rounded to whole metres. Rebuilt when the list changes size.
    private static readonly Dictionary<(int, int, int), List<SpawnResourceModel>> SpawnerIndex = new();
    private static int spawnerIndexCount = -1;
    private static IntPtr spawnerIndexList;
    private const float SamePositionSqr = 0.25f;

    internal static SpawnResourceModel? FindSpawnerModel(Vector3 position)
    {
        var list = SceneContext.Instance ? SceneContext.Instance.GameModel?.resourceSpawners : null;
        if (list == null)
            return null;

        if (list.Count != spawnerIndexCount || list.Pointer != spawnerIndexList)
        {
            SpawnerIndex.Clear();
            foreach (var model in list)
            {
                if (model == null)
                    continue;

                var key = GridKey(model.pos);
                if (!SpawnerIndex.TryGetValue(key, out var bucket))
                    SpawnerIndex[key] = bucket = new List<SpawnResourceModel>(1);
                bucket.Add(model);
            }

            spawnerIndexCount = list.Count;
            spawnerIndexList = list.Pointer;
        }

        var (x, y, z) = GridKey(position);
        SpawnResourceModel? best = null;
        var bestDistance = SamePositionSqr;

        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        for (var dz = -1; dz <= 1; dz++)
        {
            if (!SpawnerIndex.TryGetValue((x + dx, y + dy, z + dz), out var bucket))
                continue;

            foreach (var model in bucket)
            {
                var distance = (model.pos - position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = model;
            }
        }

        return best;
    }

    private static (int, int, int) GridKey(Vector3 position)
        => (Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y), Mathf.RoundToInt(position.z));

    /// <summary>
    /// The growth of every garden and resource spawner in the host's save, for a joining player.
    /// </summary>
    internal static InitialResourceSpawnersPacket CreateInitialPacket()
    {
        var entries = new List<InitialResourceSpawnersPacket.Entry>();

        var list = SceneContext.Instance ? SceneContext.Instance.GameModel?.resourceSpawners : null;
        if (list != null)
        {
            foreach (var model in list)
            {
                if (model == null)
                    continue;

                entries.Add(new InitialResourceSpawnersPacket.Entry
                {
                    Position = model.pos,
                    NextSpawnTime = model.nextSpawnTime,
                    StoredWater = model.storedWater,
                    NextSpawnRipens = model.nextSpawnRipens
                });
            }
        }

        return new InitialResourceSpawnersPacket { Entries = entries };
    }

    /// <summary>
    /// A joining player: the host's growth is written to the saved data of every spawner, loaded
    /// here or not, and kept for the spawners this save has no data for yet. The host's save is
    /// the source of truth; a loaded spawner whose owner has it live gets that owner's newer
    /// growth right after anyway. Leaving loaded ones to "their owner" lost the host's growth
    /// when nobody else had them loaded: this game took them over with its own save's growth
    /// and sent that to the host.
    /// </summary>
    internal static void ApplyInitialPacket(InitialResourceSpawnersPacket packet)
    {
        int applied = 0, live = 0, missing = 0;
        HostSpawnersNotHere.Clear();

        Dictionary<IntPtr, NetworkGarden>? gardensByModel = null;

        foreach (var entry in packet.Entries)
        {
            try
            {
                var model = FindSpawnerModel(entry.Position);
                if (model == null)
                {
                    var key = GridKey(entry.Position);
                    if (!HostSpawnersNotHere.TryGetValue(key, out var bucket))
                        HostSpawnersNotHere[key] = bucket = new List<InitialResourceSpawnersPacket.Entry>(1);
                    bucket.Add(entry);
                    missing++;
                    continue;
                }

                if (model.part == null)
                {
                    model.nextSpawnTime = entry.NextSpawnTime;
                    model.storedWater = entry.StoredWater;
                    model.nextSpawnRipens = entry.NextSpawnRipens;
                    applied++;
                    continue;
                }

                // Loaded here: its garden keeps a copy that it restores on wake, which must match.
                // One this game already simulates is left alone; the host's copy came from it.
                live++;
                gardensByModel ??= GardensByModel();
                if (gardensByModel.TryGetValue(model.Pointer, out var garden) && garden)
                {
                    if (!garden.LocallyOwned)
                        garden.ApplyHostSaved(entry.NextSpawnTime, entry.StoredWater, entry.NextSpawnRipens);
                    continue;
                }

                model.nextSpawnTime = entry.NextSpawnTime;
                model.storedWater = entry.StoredWater;
                model.nextSpawnRipens = entry.NextSpawnRipens;
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Could not apply a resource spawner from the host: {ex.Message}");
            }
        }

        SrLogger.LogDebug($"Resource spawners from the host: {applied} written, {live} loaded here (written too), {missing} not in this save yet (kept until they load)");
    }

    private static Dictionary<IntPtr, NetworkGarden> GardensByModel()
    {
        var result = new Dictionary<IntPtr, NetworkGarden>();
        foreach (var garden in Gardens.Values)
        {
            var model = garden ? garden.Model : null;
            if (model != null)
                result[model.Pointer] = garden;
        }

        return result;
    }

    #endregion
    
    internal static void Tick()
    {
        if (lastTickFrame == Time.frameCount)
            return;
        lastTickFrame = Time.frameCount;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        var now = Time.time;

        if (now >= nextClaimFlush)
        {
            nextClaimFlush = now + ClaimFlushInterval;
            FlushClaims();
        }

        if (now >= nextStateFlush)
        {
            nextStateFlush = now + StateFlushInterval;
            FlushStates();
        }
    }

    private static void FlushClaims()
    {
        if (PendingClaims.Count == 0)
            return;

        // A joining player's packets are held back until the host's world is applied. The claims
        // are kept for then: a claim is only made once, so a dropped one would be lost for good.
        if (!Main.Server.IsRunning && Main.Client.IsConnected && !NetworkActorManager.InitialActorsLoaded)
            return;

        var entries = new List<GardenOwnershipPacket.Entry>(PendingClaims.Count);
        foreach (var id in PendingClaims)
        {
            entries.Add(new GardenOwnershipPacket.Entry { GardenId = id, OwnerId = LocalID });

            if (Main.Server.IsRunning)
                KnownOwners[id] = LocalID;
        }
        PendingClaims.Clear();

        SrLogger.LogGarden($"Broadcasting {entries.Count} garden claim(s)");
        Main.SendToAllOrServer(new GardenOwnershipPacket { Entries = entries });
    }

    private static void FlushStates()
    {
        List<GardenUpdatePacket.Entry>? entries = null;

        foreach (var garden in Gardens.Values)
        {
            if (!garden || !garden.LocallyOwned || garden.IsHibernated)
                continue;

            var entry = garden.CaptureState();
            if (entry != null)
                (entries ??= new()).Add(entry.Value);
        }

        if (PendingFinalStates.Count > 0)
        {
            entries ??= new();
            entries.AddRange(PendingFinalStates.Values);
            PendingFinalStates.Clear();
        }

        if (entries != null)
        {
            SrLogger.LogGarden($"Broadcasting {entries.Count} garden state(s)");
            Main.SendToAllOrServer(new GardenUpdatePacket { Entries = entries });
        }
    }
    
    internal static void AssignOwnershipOfUnowned()
    {
        if (!Main.Server.IsRunning)
            return;

        if (SceneContext.Instance?.player == null)
            return;

        var allPlayers = new List<(string PlayerId, Vector3 Position)>
        {
            (LocalID, SceneContext.Instance.player.transform.position)
        };

        foreach (var (playerId, playerObject) in PlayerObjects)
        {
            if (playerObject)
                allPlayers.Add((playerId, playerObject.transform.position));
        }

        List<GardenOwnershipPacket.Entry>? entries = null;

        foreach (var (gardenId, owner) in KnownOwners.ToArray())
        {
            if (owner == LocalID || PlayerManager.CheckPlayerExists(owner))
                continue;

            Gardens.TryGetValue(gardenId, out var garden);

            var newOwner = LocalID;

            if (garden)
            {
                var position = garden!.transform.position;
                var bestDistance = float.MaxValue;

                foreach (var (playerId, playerPosition) in allPlayers)
                {
                    var distance = (playerPosition - position).sqrMagnitude;

                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    newOwner = playerId;
                }
            }

            KnownOwners[gardenId] = newOwner;

            SrLogger.LogGarden($"Garden '{gardenId}' reassigned from missing owner '{owner}' to '{newOwner}'");

            if (garden)
                garden!.SetOwner(newOwner);

            (entries ??= new()).Add(new GardenOwnershipPacket.Entry { GardenId = gardenId, OwnerId = newOwner });
        }

        if (entries != null)
        {
            SrLogger.LogGarden($"Broadcasting {entries.Count} garden reassignment(s) after owner left");
            Main.SendToAllOrServer(new GardenOwnershipPacket { Entries = entries });
        }
    }

    internal static void RestoreAfterTimeSkip()
    {
        foreach (var garden in Gardens.Values)
        {
            if (garden && !garden.LocallyOwned)
                garden.RestoreCachedState();
        }
    }
}