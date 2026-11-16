using System.Collections;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Components.Actor;
using SR2MP.Packets.Actor;

namespace SR2MP.Shared.Managers;

internal sealed partial class NetworkActorManager
{
    public readonly Dictionary<long, IdentifiableModel> Actors = new();

    /// <summary>
    /// True once a joining client has received and applied the host's actors (slimes, items, gadgets).
    /// Until then the client still has the actors of the save it joined with, whose ids can match
    /// completely different actors on the host, so it must not tell anyone about spawns, destroys,
    /// ownership or positions.
    /// </summary>
    internal static bool InitialActorsLoaded { get; set; }

    /// <summary>
    /// Whether this game may send actor changes (spawn, destroy, ownership, position) to others.
    /// </summary>
    internal static bool CanSendActorChanges =>
        Main.Server.IsRunning || (Main.Client.IsConnected && InitialActorsLoaded);
    public readonly Dictionary<int, IdentifiableType> ActorTypes = new();

    /// <summary>
    /// Last known owner of each actor, by actor id. The NetworkActor components that normally hold
    /// this are thrown away and recreated whenever a zone loads or the host's world is applied, and
    /// actors in zones that aren't loaded here have none at all. Without this, a player who joined,
    /// resynced or walked through a portal believed nobody owned anything and took every actor
    /// nearby, including the item another player was carrying.
    /// </summary>
    internal static readonly Dictionary<long, string> KnownOwners = new();

    /// <summary>
    /// Radiant look of slimes that exist here only as data (their zone is not loaded on this
    /// machine), by actor id. Applied when the slime is built, and sent on to joining players.
    /// </summary>
    internal static readonly Dictionary<long, SR2MP.Packets.Actor.ActorAppearanceType> PendingRadiancy = new();

    internal static void RememberOwner(long actorId, string? ownerId)
    {
        if (actorId == 0)
            return;

        if (string.IsNullOrEmpty(ownerId))
            KnownOwners.Remove(actorId);
        else
            KnownOwners[actorId] = ownerId;
    }

    /// <summary>
    /// The owner of every actor this game knows about, for a player who is joining or resyncing.
    /// </summary>
    internal Dictionary<long, string> CollectOwners()
    {
        var owners = new Dictionary<long, string>(KnownOwners);

        foreach (var (id, model) in Actors)
        {
            try
            {
                if (model == null || !model.TryGetNetworkComponent(out var netActor))
                    continue;

                var owner = netActor.LocallyOwned ? LocalID : netActor.CurrentOwnerId;

                if (!string.IsNullOrEmpty(owner))
                    owners[id] = owner;
            }
            catch { /* ignored */ }
        }

        return owners;
    }

    /// <summary>
    /// Multiplayer ended (server closed or we disconnected). Actors other players were simulating
    /// are frozen on our side and nobody will move them anymore, so take them all back.
    /// </summary>
    internal void ReleaseAllToLocal()
    {
        KnownOwners.Clear();
        ClearDeferredWorldChanges();

        foreach (var model in Actors.Values.ToArray())
        {
            try
            {
                if (model == null || !model.TryGetNetworkComponent(out var netActor))
                    continue;

                netActor.CurrentOwnerId = string.Empty;
                netActor.LocallyOwned = true;
            }
            catch { /* ignored */ }
        }
    }

    public static int GetPersistentID(IdentifiableType type)
        => GameContext.Instance.AutoSaveDirector._saveReferenceTranslation.GetPersistenceId(type);

    internal void Initialize(GameContext context)
    {
        ActorTypes.Clear();
        Actors.Clear();

        foreach (var type in context.AutoSaveDirector._saveReferenceTranslation._identifiableTypeLookup)
            ActorTypes.TryAdd(GetPersistentID(type.value), type.value);

        ActorTypes[-1] = null!;

        StartCoroutine(ZoneLoadingLoop());
        StartCoroutine(OwnUnownedSlimesLoop());
    }

    private IEnumerator OwnUnownedSlimesLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(2f);

            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                continue;

            if (!SystemContext.Instance.SceneLoader.IsCurrentSceneGroupGameplay())
                continue;

            if (SceneContext.Instance?.player == null)
                continue;

            TakeOwnershipOfNearby(true);
        }
    }

    private IEnumerator ZoneLoadingLoop()
    {
        while (true)
        {
            yield return new WaitForSceneGroupLoad(false);
            yield return new WaitForSceneGroupLoad();

            if (!Main.Server.IsRunning && !Main.Client.IsConnected)
                continue;

            if (!SystemContext.Instance.SceneLoader.IsCurrentSceneGroupGameplay())
                continue;

            var gameModel = SceneContext.Instance?.GameModel;
            if (!gameModel)
                continue;

            try
            {
                RebuildActorsForCurrentScene(gameModel!);
            }
            catch (Exception ex)
            {
                // This loop must survive: if it stops, nothing in a newly loaded zone is synced for
                // the rest of the session.
                HandlingPacket = false;
                SrLogger.LogError($"Rebuilding actors after a zone load failed: {ex}");
            }

            try
            {
                TakeOwnershipOfNearby();
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"Taking ownership after a zone load failed: {ex}");
            }
        }
    }

    private void RebuildActorsForCurrentScene(GameModel gameModel)
    {
        var scene = SystemContext.Instance.SceneLoader.CurrentSceneGroup;

        // Copy first: destroying and building objects below can change the game's table.
        var models = new List<ActorModel>();
        foreach (var actor in gameModel.identifiables)
        {
            var value = actor.value;
            if (value == null || value.ident == null || value.ident.IsPlayer)
                continue;

            var actorModel = value.TryCast<ActorModel>();
            if (actorModel != null)
                models.Add(actorModel);
        }

        foreach (var model in models)
        {
            try
            {
                var obj = model.GetGameObject();
                if (!obj)
                    continue;

                Object.Destroy(obj);
                Actors.Remove(model.actorId.Value);
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not clear actor before rebuilding a zone: {ex.Message}");
            }
        }

        var failures = 0;
        foreach (var model in models)
        {
            try
            {
                if (!model.ident.prefab)
                    continue;

                if (model.sceneGroup != scene)
                    continue;

                GameObject obj;
                HandlingPacket = true;
                try
                {
                    obj = InstantiationHelpers.InstantiateActorFromModel(model);
                }
                finally
                {
                    HandlingPacket = false;
                }

                if (!obj)
                    continue;

                var networkComponent = obj.GetComponent<NetworkActor>();
                if (!networkComponent)
                    networkComponent = obj.AddComponent<NetworkActor>();

                networkComponent.previousPosition = model.lastPosition;
                networkComponent.nextPosition     = model.lastPosition;
                networkComponent.previousRotation = model.lastRotation;
                networkComponent.nextRotation     = model.lastRotation;

                Actors[model.actorId.Value] = model;

                if (PendingRadiancy.TryGetValue(model.actorId.Value, out var radiancy))
                {
                    var slime = model.TryCast<SlimeModel>();
                    if (slime == null || ApplyRadiancy(slime, radiancy))
                        PendingRadiancy.Remove(model.actorId.Value);
                }
            }
            catch (Exception ex)
            {
                if (failures++ < 5)
                    SrLogger.LogWarning($"Could not rebuild an actor after a zone load: {ex.Message}");
            }
        }

        if (failures > 5)
            SrLogger.LogWarning($"{failures} actors could not be rebuilt after the zone load.");
    }

    /// <summary>
    /// Keeps this game's id counter above an id that arrived from the network, when that id is in
    /// this player's own range. Otherwise the game can later hand out the same id again, and
    /// registering two actors under one id fails inside the game's own loading code.
    /// </summary>
    internal static void ReserveActorId(long id)
    {
        try
        {
            var provider = GameState?._actorIdProvider;
            if (provider == null)
                return;

            var next = provider._nextActorId;
            if (id < next || id / ActorIdOffset != next / ActorIdOffset)
                return;

            provider._nextActorId = id + 1;
        }
        catch
        {
            // ignored
        }
    }

    // Actor spawns and destroys that arrived while this game was loading a zone. The game fills
    // its actor table over several frames during a load; changing that table underneath it can
    // make the load fail, and a failed load sends the player back to the main menu.
    private static readonly Queue<Action> DeferredWorldChanges = new();
    private static float deferredSince;
    private const float MaxDeferSeconds = 60f;
    private const int MaxDeferredChanges = 20000;

    private static bool IsSceneLoading()
    {
        try
        {
            var loader = SystemContext.Instance != null ? SystemContext.Instance.SceneLoader : null;
            return loader != null && loader.IsSceneLoadInProgress;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Queues <paramref name="apply"/> if a zone is loading. Returns false when the caller should apply it now.
    /// </summary>
    internal static bool DeferDuringSceneLoad(Action apply)
    {
        if (!IsSceneLoading())
        {
            // Keep the order: anything still queued goes first.
            RunDeferredWorldChanges(true);
            return false;
        }

        if (DeferredWorldChanges.Count == 0)
            deferredSince = UnityEngine.Time.realtimeSinceStartup;

        DeferredWorldChanges.Enqueue(apply);
        return true;
    }

    internal static void RunDeferredWorldChanges(bool force = false)
    {
        if (DeferredWorldChanges.Count == 0)
            return;

        if (!force && IsSceneLoading()
            && UnityEngine.Time.realtimeSinceStartup - deferredSince < MaxDeferSeconds
            && DeferredWorldChanges.Count < MaxDeferredChanges)
            return;

        while (DeferredWorldChanges.TryDequeue(out var apply))
        {
            try
            {
                apply();
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"A queued actor change failed: {ex.Message}");
            }
            finally
            {
                HandlingPacket = false;
            }
        }
    }

    internal static void ClearDeferredWorldChanges() => DeferredWorldChanges.Clear();

    internal static bool IsSceneLoadingNow => IsSceneLoading();

    internal static bool IsPlayerActor(ActorId id)
    {
        try
        {
            return GameState.TryGetIdentifiableModel(id, out var model)
                   && model != null && model.ident != null && model.ident.IsPlayer;
        }
        catch
        {
            return false;
        }
    }

    private static bool ActorIDAlreadyInUse(ActorId id)
        => SceneContext.Instance?.GameModel?.TryGetIdentifiableModel(id, out _) ?? false;

    public static long GetHighestActorIdInRange(long min, long max)
    {
        var result = min;
        foreach (var actor in GameState.identifiables)
        {
            var id = actor.value.actorId.Value;
            if (id < min || id >= max)
                continue;
            if (id > result)
                result = id;
        }

        return result;
    }
    
    internal void TakeOwnershipOfNearby(bool onlyUnownedSlimes = false)
    {
        if (!CanSendActorChanges)
            return;

        var boundsSize = new Vector3(600, 1250, 600);
        var ownershipBoundsSize = new Vector3(200, 1250, 200);
        var bounds = new Bounds(SceneContext.Instance.player.transform.position, boundsSize);

        foreach (var actor in Actors.Values.ToArray())
        {
            try
            {
                if (actor == null)
                    continue;

                if (!bounds.Contains(actor.lastPosition))
                    continue;

                if (!actor.TryGetNetworkComponent(out var netActor))
                    continue;

                netActor.EnsureOwnerInfo();

                if (!onlyUnownedSlimes)
                {
                    // Never take an actor from another player who is actively simulating it (they may be carrying it).
                    if (netActor.IsActivelyOwnedByOtherPlayer)
                        continue;

                    // Asleep on our side: we would own it without simulating it, and it would sit frozen
                    // for whoever is actually near it. It is claimed when it wakes up here.
                    var regionMember = netActor.RegionMember ? netActor.RegionMember : netActor.GetComponent<Il2CppMonomiPark.SlimeRancher.Regions.RegionMember>();
                    if (regionMember && regionMember!._hibernating)
                        continue;
                }

                if (onlyUnownedSlimes)
                {
                    if (netActor.LocallyOwned)
                        continue;

                    if (netActor.RegionMember?._hibernating == true)
                        continue;

                    var ownerId = netActor.CurrentOwnerId;
                    var ownerExists = !string.IsNullOrEmpty(ownerId) && PlayerManager.CheckPlayerExists(ownerId);

                    // Plorts without a live owner used to be destroyed here, and the destroy was broadcast,
                    // which wiped plorts loaded from the save (they have no owner yet) for everyone.
                    // Taking them over like any other unowned actor is enough.

                    if (ownerExists
                        && netActor.OwnerRecentlyHeard
                        && PlayerObjects.TryGetValue(ownerId, out var playerObj)
                        && playerObj
                        && new Bounds(playerObj.transform.position, ownershipBoundsSize).Contains(actor.lastPosition))
                        continue;
                }

                var actorId = netActor.ActorId;
                if (actorId.Value == 0)
                    continue;

                var packet = new ActorTransferPacket { ActorId = actorId, OwnerId = LocalID };
                ApplyOwnership(packet);
                Main.SendToAllOrServer(packet);
            }
            catch { /* ignored */ }
        }
    }
    
    internal void AssignOwnershipOfUnowned()
    {
        if (!CanSendActorChanges)
            return;

        if (SceneContext.Instance?.player == null)
            return;

        var bounds = new Vector3(600, 1250, 600);

        var allPlayers = new List<(string PlayerId, Vector3 Position)>
        {
            (LocalID, SceneContext.Instance.player.transform.position)
        };

        foreach (var (playerId, playerObject) in PlayerObjects)
        {
            if (playerObject)
                allPlayers.Add((playerId, playerObject.transform.position));
        }

        foreach (var actor in Actors.Values.ToArray())
        {
            try
            {
                if (actor == null)
                    continue;

                if (!actor.TryGetNetworkComponent(out var netActor))
                    continue;

                netActor.EnsureOwnerInfo();

                if (netActor.LocallyOwned || netActor.CurrentOwnerId == LocalID || PlayerManager.CheckPlayerExists(netActor.CurrentOwnerId))
                    continue;

                var actorId = netActor.ActorId;
                if (actorId.Value == 0)
                    continue;

                var position = actor.lastPosition;
                string? newOwner = null;
                var bestDistance = float.MaxValue;

                foreach (var (playerId, playerPosition) in allPlayers)
                {
                    if (!new Bounds(playerPosition, bounds).Contains(position))
                        continue;

                    var distance = (playerPosition - position).sqrMagnitude;

                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    newOwner = playerId;
                }

                if (newOwner == null)
                    continue;

                var packet = new ActorTransferPacket { ActorId = actorId, OwnerId = newOwner };
                ApplyOwnership(packet);
                Main.SendToAllOrServer(packet);
            }
            catch { /* ignored */ }
        }
    }

    public static T[] GetAllOfType<T>() where T : IdentifiableModel
    {
        var result = new List<T>();

        var identifiables = sceneContext?.GameModel?.identifiables;

        if (identifiables == null)
            return result.ToArray();

        foreach (var ident in identifiables)
        {
            var casted = ident.value.TryCast<T>(); 
            if (casted != null)
                result.Add(casted);
        }
        return result.ToArray();
    }
    
}