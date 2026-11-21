using Il2CppMonomiPark.SlimeRancher.Regions;
using JetBrains.Annotations;
using SR2MP.Packets.LandPlots;
using SR2MP.Shared.Managers;
using Starlight.Storage;

namespace SR2MP.Components.LandPlots;

[InjectIntoIL]
internal sealed class NetworkGarden : MonoBehaviour
{
    private SpawnResource? garden;
    private RegionMember regionMember;

    // The area the garden is in. The game stops a spawner while its area sleeps (SpawnResource
    // reads this same area); garden crops have no RegionMember, so following only that never saw
    // the garden sleep: the host kept owning every garden while away from the ranch, and gardens
    // stopped growing for the players who were there.
    private Region? region;
    private Region.OnHibernationStateChangedDelegate? regionHandler;
    private float nextHibernationCheck;
    private const float HibernationCheckInterval = 1f;

    private bool registered;
    private int registerAttempts;
    private bool claimOnReady;
    private bool locallyOwned;
    private bool vanillaAllowSpawningInFastForwarding;
    private float lastActivityTime;

    private const float TakeoverDelayMin = 12f;
    private const float TakeoverDelayMax = 16f;
    private readonly float takeoverDelay = UnityEngine.Random.Range(TakeoverDelayMin, TakeoverDelayMax);

    private double cachedNextSpawnTime;
    private float cachedStoredWater;
    private bool cachedNextSpawnRipens;
    private bool hasCachedState;
    
    private string? resolvedId;
    internal string? Id
    {
        get
        {
            if (!string.IsNullOrEmpty(resolvedId))
                return resolvedId;

            var landPlot = GetComponentInParent<LandPlotLocation>();
            if (landPlot != null && !string.IsNullOrEmpty(landPlot._id) && garden != null)
            {
                var siblings = landPlot.GetComponentsInChildren<SpawnResource>(true);
                var gardenInstanceId = garden.GetInstanceID();
                var index = 0;
                for (var i = 0; i < siblings.Length; i++)
                {
                    if (siblings[i] != null && siblings[i].GetInstanceID() == gardenInstanceId)
                    {
                        index = i;
                        break;
                    }
                }
                resolvedId = $"{landPlot._id}_{index}";
            }
            else if (!string.IsNullOrEmpty(garden?._id))
                resolvedId = garden!._id;

            return resolvedId;
        }
    }

    public bool IsHibernated { get; private set; }
    private string CurrentOwnerId { get; set; } = string.Empty;

    /// <summary>This game is the garden's owner, whether or not it simulates it right now.</summary>
    internal bool OwnedHere => CurrentOwnerId == LocalID;

    public bool LocallyOwned
    {
        get => locallyOwned;
        private set
        {
            locallyOwned = value;
            ApplySimulationGate();
        }
    }

    private void ApplySimulationGate()
    {
        if (garden == null)
            return;

        garden.enabled = locallyOwned;
        garden._allowSpawningInFastForwarding = locallyOwned;
    }

    [UsedImplicitly]
    public void Awake()
    {
        garden = GetComponent<SpawnResource>();
        regionMember = GetComponentInParent<RegionMember>(true) ?? GetComponentInChildren<RegionMember>(true);

        if (garden != null)
            vanillaAllowSpawningInFastForwarding = garden._allowSpawningInFastForwarding;

        region = garden != null ? garden._region : null;
        IsHibernated = AreaAsleep();
        lastActivityTime = UnityEngine.Time.time;

        if (Main.Server.IsRunning)
        {
            CurrentOwnerId = LocalID;
            LocallyOwned = true;
        }
        else
        {
            LocallyOwned = false;
        }
    }

    public void Start()
        => SetupHibernationEvent();

    public void Update()
    {
        NetworkGardenManager.Tick();

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        // Backs up the area's sleep event, which a garden made inside a sleeping area never gets.
        if (region != null && UnityEngine.Time.time >= nextHibernationCheck)
        {
            nextHibernationCheck = UnityEngine.Time.time + HibernationCheckInterval;
            var asleep = AreaAsleep();
            if (asleep != IsHibernated)
                OnHibernationChanged(asleep);
        }

        if (!registered)
        {
            TryRegister();
            return;
        }

        if (LocallyOwned || IsHibernated)
            return;

        if (!Main.Server.IsRunning && !NetworkActorManager.InitialActorsLoaded)
        {
            lastActivityTime = UnityEngine.Time.time;
            return;
        }

        if (UnityEngine.Time.time - lastActivityTime < takeoverDelay)
            return;
        
        SrLogger.LogGarden($"Garden '{Id}' silent for {takeoverDelay:F0}s, taking over");
        TakeOwnership();
    }
    private void TryRegister()
    {
        if (garden == null || string.IsNullOrEmpty(Id) || garden._model == null)
        {
            if (++registerAttempts == 60)
                SrLogger.LogGarden($"Garden register STUCK: name='{name}', id='{Id ?? "<null>"}', modelNull={garden?._model == null}");
            return;
        }

        registered = true;

        // The host's saved growth for a spawner this save had no data for when this player joined.
        // Without it the spawner started on this save's defaults, and this game sent those to the
        // host as the owner's growth once it took the spawner over.
        if (NetworkGardenManager.TakeHostSpawnerState(garden._model.pos, out var hostSaved))
        {
            ApplyHostSaved(hostSaved.NextSpawnTime, hostSaved.StoredWater, hostSaved.NextSpawnRipens);
            SrLogger.LogGarden($"Garden '{Id}' got the host's saved growth");
        }

        // Growth another player simulated while this garden had no saved data here yet. Written
        // to the data directly: the host's gardens start as its own, so ApplyUpdate would skip it.
        if (NetworkGardenManager.TakePendingState(Id!, out var pending))
        {
            garden._model.nextSpawnTime = pending.NextSpawnTime;
            garden._model.storedWater = pending.StoredWater;
            garden._model.nextSpawnRipens = pending.NextSpawnRipens;
            CacheState(pending.NextSpawnTime, pending.StoredWater, pending.NextSpawnRipens);
            SrLogger.LogGarden($"Garden '{Id}' got the growth sent while it was not loaded");
        }

        NetworkGardenManager.Register(this);
        ApplySimulationGate();

        SrLogger.LogGarden($"Garden registered: name='{name}', id='{Id}', locallyOwned={LocallyOwned}");

        if (claimOnReady)
        {
            claimOnReady = false;
            TakeOwnership();
        }

        // Made or loaded inside a sleeping area (the host's gardens start as its own): let the
        // players who are there run it.
        if (IsHibernated && LocallyOwned)
            ReleaseWhileAsleep();
    }

    private bool AreaAsleep()
    {
        try
        {
            return region != null && region.Hibernated;
        }
        catch
        {
            return false;
        }
    }

    internal void TakeOwnership(bool broadcast = true)
    {
        CurrentOwnerId = LocalID;
        LocallyOwned = true;
        lastActivityTime = UnityEngine.Time.time;

        if (Main.Server.IsRunning && !string.IsNullOrEmpty(Id))
            NetworkGardenManager.RecordHostOwner(Id!);

        SrLogger.LogGarden($"Garden '{Id}' claimed by '{LocalID}' (broadcast={broadcast}, nextSpawnTime={garden?._model?.nextSpawnTime})");

        if (!broadcast)
            return;

        NetworkGardenManager.QueueClaim(Id!);
    }
    
    internal void SetOwner(string ownerId)
    {
        CurrentOwnerId = ownerId;
        LocallyOwned = ownerId == LocalID && !IsHibernated;
        lastActivityTime = UnityEngine.Time.time;

        SrLogger.LogGarden($"Garden '{Id}' owner set to '{ownerId}' (locallyOwned={LocallyOwned})");
    }

    internal void ClaimOnReady()
    {
        if (registered)
        {
            TakeOwnership();
            return;
        }

        claimOnReady = true;
    }
    
    internal void OnOwnerReleased()
    {
        // Our own release coming back from the host while we are away: we stay the owner to come
        // back to if nobody takes the garden in the meantime.
        if (!(IsHibernated && CurrentOwnerId == LocalID))
            CurrentOwnerId = string.Empty;

        // A joining game waits for the host's world: its own save's growth must not win.
        var worldReady = Main.Server.IsRunning || NetworkActorManager.InitialActorsLoaded;
        if (!IsHibernated && registered && worldReady)
        {
            SrLogger.LogGarden($"Garden '{Id}' released by owner, taking over");
            TakeOwnership();
        }
        else
        {
            // The silence takeover picks it up once this game is ready for it.
            lastActivityTime = UnityEngine.Time.time;
            SrLogger.LogGarden($"Garden '{Id}' released by owner, not taking over (hibernated={IsHibernated}, registered={registered}, worldReady={worldReady})");
        }
    }

    /// <summary>A game joining a host: nothing here is ours until the host says so.</summary>
    internal void ResetForJoin()
    {
        CurrentOwnerId = string.Empty;
        LocallyOwned = false;
        lastActivityTime = UnityEngine.Time.time;
    }

    internal void ResetToVanilla()
    {
        hasCachedState = false;
        locallyOwned = false;
        CurrentOwnerId = string.Empty;

        if (garden != null)
        {
            garden.enabled = true;
            garden._allowSpawningInFastForwarding = vanillaAllowSpawningInFastForwarding;
        }
    }

    private void SetupHibernationEvent()
    {
        if (region != null)
        {
            try
            {
                regionHandler = new Action<bool>(OnHibernationChanged);
                region.add_OnBeforeHibernationStateChanged(regionHandler);
                return;
            }
            catch (Exception ex)
            {
                regionHandler = null;
                SrLogger.LogDebug($"NetworkGarden: could not follow the area's sleep, checking it every second instead: {ex.Message}");
                return;
            }
        }

        if (regionMember == null)
        {
            if (GardenLogging)
                SrLogger.LogWarning($"Garden '{Id}' has no RegionMember");
            return;
        }

        try
        {
            regionMember.add_BeforeHibernationChanged(
                Il2CppSystem.Delegate.CreateDelegate(
                    Il2CppSystem.Type.GetType("MonomiPark.SlimeRancher.Regions.RegionMember")
                        .GetEvent("BeforeHibernationChanged").EventHandlerType,
                    Cast<Il2CppSystem.Object>(),
                    nameof(OnHibernationChanged),
                    true
                ).Cast<RegionMember.OnHibernationChange>()
            );
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"NetworkGarden: Failed to add hibernation event: {ex.Message}");
        }
    }

    public void OnHibernationChanged(bool hibernating)
    {
        if (hibernating == IsHibernated)
            return;

        IsHibernated = hibernating;

        // The area's event keeps coming after leaving a game; owning is only for a session, and so
        // is the growth kept for waking (the game grows the garden itself meanwhile).
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
        {
            hasCachedState = false;
            return;
        }

        SrLogger.LogGarden($"Garden '{Id}' hibernation changed: hibernating={hibernating}, locallyOwned={LocallyOwned}");

        if (hibernating)
        {
            if (garden?._model != null)
                CacheState(garden._model.nextSpawnTime, garden._model.storedWater, garden._model.nextSpawnRipens);

            if (!LocallyOwned || !registered)
                return;

            ReleaseWhileAsleep();
        }
        else
        {
            RestoreCachedState();
            lastActivityTime = UnityEngine.Time.time;

            // Runs before the area's catch-up, so the owner's game is the one that catches up. A
            // garden nobody owns (released while everyone was away) goes to the first game to wake it.
            // A joining game waits for the host's world: its own save's growth must not win.
            var worldReady = Main.Server.IsRunning || NetworkActorManager.InitialActorsLoaded;
            if (registered && (CurrentOwnerId == LocalID || (string.IsNullOrEmpty(CurrentOwnerId) && worldReady)))
            {
                SrLogger.LogGarden($"Garden '{Id}' ownership resumed on wake");
                TakeOwnership();
            }
        }
    }

    private void ReleaseWhileAsleep()
    {
        NetworkGardenManager.QueueFinalState(this);
        LocallyOwned = false;

        // So others can take over immediately without having to wait the delay.
        // CurrentOwnerId stays ours: if nobody claims it while we're away, we resume on wake.
        NetworkGardenManager.BroadcastRelease(Id!);
        SrLogger.LogGarden($"Garden '{Id}' released on hibernation");
    }

    public void ApplyUpdate(double nextSpawnTime, float storedWater, bool nextSpawnRipens)
    {
        lastActivityTime = UnityEngine.Time.time;

        if (LocallyOwned)
            return;

        CacheState(nextSpawnTime, storedWater, nextSpawnRipens);

        if (garden?._model == null)
            return;

        garden._model.nextSpawnTime = nextSpawnTime;
        garden._model.storedWater = storedWater;
        garden._model.nextSpawnRipens = nextSpawnRipens;
    }

    internal Il2CppMonomiPark.SlimeRancher.DataModel.SpawnResourceModel? Model
        => garden != null ? garden._model : null;

    // The host's saved growth, written whoever owns the garden here: the host's save is the
    // source of truth, and a live owner elsewhere sends its newer growth right after.
    internal void ApplyHostSaved(double nextSpawnTime, float storedWater, bool nextSpawnRipens)
    {
        if (garden?._model == null)
            return;

        garden._model.nextSpawnTime = nextSpawnTime;
        garden._model.storedWater = storedWater;
        garden._model.nextSpawnRipens = nextSpawnRipens;
        CacheState(nextSpawnTime, storedWater, nextSpawnRipens);
    }

    internal GardenUpdatePacket.Entry? CaptureState()
    {
        if (garden?._model == null || string.IsNullOrEmpty(Id))
            return null;

        CacheState(garden._model.nextSpawnTime, garden._model.storedWater, garden._model.nextSpawnRipens);

        return new GardenUpdatePacket.Entry
        {
            GardenId = Id!,
            NextSpawnTime = garden._model.nextSpawnTime,
            StoredWater = garden._model.storedWater,
            NextSpawnRipens = garden._model.nextSpawnRipens,
            Position = garden._model.pos
        };
    }

    private void CacheState(double nextSpawnTime, float storedWater, bool nextSpawnRipens)
    {
        cachedNextSpawnTime = nextSpawnTime;
        cachedStoredWater = storedWater;
        cachedNextSpawnRipens = nextSpawnRipens;
        hasCachedState = true;
    }

    public void RestoreCachedState()
    {
        if (!hasCachedState || garden?._model == null)
            return;

        garden._model.nextSpawnTime = cachedNextSpawnTime;
        garden._model.storedWater = cachedStoredWater;
        garden._model.nextSpawnRipens = cachedNextSpawnRipens;
    }

    [UsedImplicitly]
    public void OnDestroy()
    {
        if (region != null && regionHandler != null)
        {
            try
            {
                region.remove_OnBeforeHibernationStateChanged(regionHandler);
            }
            catch
            {
                // the area is going away too
            }
        }

        NetworkGardenManager.Unregister(this);
    }
}