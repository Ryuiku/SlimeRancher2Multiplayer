using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Slime.Tangle;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;
using SR2MP.Components.Actor;
using SR2MP.Packets.Slime.Tangle;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Shows every player a tangle's bud stalks and grab vine. Only one game grows a stalk (the tangle's
/// owner, or the host for tangle weather) and only the tangle's owner fires the grab vine; the bud on
/// the stalk is a normal shared actor. The others get a copy of the stalk or vine that only plays its
/// animation: a copied stalk never grows a bud of its own, and a copied vine holds nothing.
/// </summary>
internal static class NetworkTangleManager
{
    private sealed class LocalStalk
    {
        public long Id;
        public bool OutSent;
        public VineBulbWithStalk? Stalk;
        // Game time when the stalk started here, so a repeat tells only the seed time left.
        public float StartedAt;
        // The stalk's bud once it grew, so a bud that is gone (picked while asleep here) is noticed.
        public GameObject? Bud;
    }

    private sealed class RemoteStalk
    {
        public long Id;
        public VineBulbWithStalk? Stalk;
        public float LastHeard;
        // Until when the copy is still growing in; an Out before then removes it outright, since
        // the copy's own grow-in would play over the withering.
        public float GrownAt;
        public bool Withering;
        // When it began withering: the game removes it after the wither, but if the game ignored
        // the wither (still growing in), it is removed after a while anyway.
        public float WitherStartedAt;
    }

    // How often the grower repeats each standing stalk, and when a copy that heard nothing goes.
    private const float StalkResendSeconds = 6f;
    private const float StalkExpireSeconds = 20f;

    // A stalk's grow-in (and wither) animation: the game waits this long before it accepts the
    // next one, and ignores a wither asked for meanwhile.
    private const float StalkAnimationSeconds = 4.5f;

    // A copy still there this long after it began withering is removed.
    private const float WitherTimeoutSeconds = 10f;

    // A copied grab vine is removed after this long even if the owner's "gone" never arrives.
    private const float RemoteVineLifetime = 15f;

    // Stalks this game grew, by the stalk's native pointer.
    private static readonly Dictionary<IntPtr, LocalStalk> LocalStalks = new();

    // Copies of other players' stalks, by the grower's stalk id, and the copies' pointers.
    private static readonly Dictionary<long, RemoteStalk> RemoteStalks = new();
    private static readonly Dictionary<IntPtr, long> RemoteStalkIds = new();

    // Stalks that withered or went: a repeat still on its way must not bring them back.
    private static readonly HashSet<long> EndedStalks = new();

    // Buds hanging on other players' stalks: bud actor id -> its stalk and when the grower last
    // said so. Kept apart from the copies, so a bud is left alone even where no copy is shown
    // (another area, still loading, prefab missing). Each stalk's bud, the other way round.
    private static readonly Dictionary<long, (long StalkId, float LastHeard)> HangingBuds = new();
    private static readonly Dictionary<long, long> StalkBuds = new();

    // The bud hanging on each stalk this game grew, by the bud object's pointer.
    private static readonly Dictionary<IntPtr, VineBulbWithStalk> LocalBuds = new();

    // Tangles whose vine this game fired and announced: actor id -> vine id.
    private static readonly Dictionary<long, long> LocalVines = new();

    // Copies of other players' grab vines, by tangle actor id.
    private static readonly Dictionary<long, (GameObject Copy, long VineId)> RemoteVines = new();

    // Stalk prefabs by name.
    private static readonly Dictionary<string, GameObject> StalkPrefabs = new();
    private static readonly HashSet<string> LoggedMisses = new();

    // Animator parameter types by hash (the vine's drop parameter).
    private static readonly Dictionary<int, AnimatorControllerParameterType> ParameterTypes = new();

    private static long sessionPrefix = NewSessionPrefix();
    private static long idCounter;
    private static float nextResend;
    private static bool scanForStalks;

    private static bool InMultiplayer => Main.Server.IsRunning || Main.Client.IsConnected;

    private static long NewSessionPrefix() => (long)System.Random.Shared.Next(1, int.MaxValue) << 32;

    private static long NextId() => sessionPrefix | ++idCounter;

    /// <summary>Whether this actor is a bud hanging on another player's stalk.</summary>
    public static bool IsHangingBud(long actorId)
        => HangingBuds.Count > 0 && HangingBuds.ContainsKey(actorId);

    #region Stalks: the game that grew them

    public static bool IsRemoteCopy(VineBulbWithStalk stalk)
        => stalk && RemoteStalkIds.ContainsKey(stalk.Pointer);

    /// <summary>A stalk started here. If this game grew it, the others are told.</summary>
    public static void OnStalkStarted(VineBulbWithStalk stalk)
    {
        if (!InMultiplayer || !stalk || IsRemoteCopy(stalk))
            return;

        var local = Register(stalk);
        if (local != null)
            SendSpawn(local);
    }

    private static LocalStalk? Register(VineBulbWithStalk stalk)
    {
        var pointer = stalk.Pointer;
        if (LocalStalks.ContainsKey(pointer))
            return null;

        var local = new LocalStalk { Id = NextId(), Stalk = stalk, StartedAt = UnityEngine.Time.time };
        LocalStalks[pointer] = local;

        var bud = stalk._budObj;
        if (bud)
        {
            LocalBuds[bud.Pointer] = stalk;
            local.Bud = bud;
        }

        return local;
    }

    private static void SendSpawn(LocalStalk local)
    {
        var stalk = local.Stalk;
        if (!stalk)
            return;

        var transform = stalk!.transform;
        var sceneGroup = stalk._sceneGroup;
        var seedLeft = stalk._shouldSpawnSeed && !stalk._budObj
            ? stalk._seedDuration - (UnityEngine.Time.time - local.StartedAt)
            : 0f;

        // The bud is announced as hanging only while it is still ours: once someone picked it,
        // the others must be free to take it over.
        long budActorId = 0;
        var bud = stalk._budObj;
        if (bud)
        {
            var budActor = bud.GetComponent<NetworkActor>();
            if (budActor && !OwnedByOther(budActor!))
                budActorId = budActor!.ActorId.Value;
        }

        Main.SendToAllOrServer(new TangleStalkPacket
        {
            Event = TangleStalkPacket.StalkEvent.Spawn,
            StalkId = local.Id,
            PrefabName = PrefabNameOf(stalk.gameObject),
            SceneGroup = sceneGroup ? sceneGroup.name : CurrentSceneGroupName(),
            Position = transform.position,
            Rotation = transform.rotation,
            // A stalk that already has its bud, or is past its seed, is shown grown with no seed
            // phase first; one still seeding sends only the seed time left.
            ShouldSpawnSeed = seedLeft > 0f,
            SeedDuration = seedLeft,
            IsBigSeed = stalk._isBigSeed,
            BudActorId = budActorId
        });
    }

    /// <summary>A stalk this game grew hung its bud: the others learn which bud is on it.</summary>
    public static void OnStalkSpawnedBud(VineBulbWithStalk stalk)
    {
        if (!InMultiplayer || !stalk || !LocalStalks.TryGetValue(stalk.Pointer, out var local))
            return;

        var bud = stalk._budObj;
        if (!bud)
            return;

        LocalBuds[bud.Pointer] = stalk;
        local.Bud = bud;
        if (!local.OutSent)
            SendSpawn(local);
    }

    /// <summary>A stalk starts withering. If this game grew it, the others' copies wither too.</summary>
    public static void OnStalkOut(VineBulbWithStalk stalk, bool silent)
    {
        if (!InMultiplayer || !stalk)
            return;

        if (!LocalStalks.TryGetValue(stalk.Pointer, out var local) || local.OutSent)
            return;

        local.OutSent = true;
        Main.SendToAllOrServer(new TangleStalkPacket
        {
            Event = TangleStalkPacket.StalkEvent.Out,
            StalkId = local.Id,
            Silent = silent
        });
    }

    /// <summary>A stalk is destroyed here: its own copy, or one this game grew.</summary>
    public static void OnStalkDestroyed(VineBulbWithStalk stalk)
    {
        if (stalk == null)
            return;

        var pointer = stalk.Pointer;

        // A copy went (its area unloaded here, or it withered). Its bud stays marked as hanging
        // while the grower keeps repeating the stalk.
        if (RemoteStalkIds.Remove(pointer, out var remoteId))
        {
            RemoteStalks.Remove(remoteId);
            return;
        }

        if (!LocalStalks.Remove(pointer, out var local))
            return;

        RemoveBudsOf(pointer);

        // A stalk that withered was already shown withering; one that went without it (its area
        // unloaded, or the game removed it) is removed on the others too.
        if (local.OutSent || !InMultiplayer)
            return;

        Main.SendToAllOrServer(new TangleStalkPacket
        {
            Event = TangleStalkPacket.StalkEvent.Remove,
            StalkId = local.Id
        });
    }

    /// <summary>
    /// A bud on a stalk this game grew. Once another player has taken the bud (picked it with the
    /// vac; other players leave hanging buds alone otherwise), the bud is let go here as the game
    /// would when it is picked, and the stalk withers. Otherwise the joint would keep the bud tied to
    /// this stalk while the other player pulls it away.
    /// </summary>
    public static void CheckLocalBud(GameObject bud)
    {
        if (LocalBuds.Count == 0 || !bud)
            return;

        var pointer = bud.Pointer;
        if (!LocalBuds.TryGetValue(pointer, out var stalk))
            return;

        if (!stalk)
        {
            LocalBuds.Remove(pointer);
            return;
        }

        var networkActor = bud.GetComponent<NetworkActor>();
        if (!networkActor || !OwnedByOther(networkActor!))
            return;

        LocalBuds.Remove(pointer);

        try
        {
            // The game's own handler for a picked bud: drops the joints, unparents the bud and
            // withers the stalk (which tells the others).
            stalk.OnDetached(false);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle: letting go of a bud another player took failed: {ex.Message}");
        }
    }

    // Another player owns it. Not just "not locally owned": a sleeping or just-woken bud is briefly
    // not locally owned here while it is still ours.
    private static bool OwnedByOther(NetworkActor networkActor)
    {
        if (networkActor.LocallyOwned)
            return false;

        var owner = networkActor.CurrentOwnerId;
        return !string.IsNullOrEmpty(owner) && owner != LocalID;
    }

    /// <summary>
    /// The bud's own Update does not run while it sleeps here, so a bud picked by another player
    /// meanwhile is let go from the tick: if another player owns it, it is released as when picked;
    /// if it is gone altogether, the stalk withers.
    /// </summary>
    private static void CheckSleepingBud(LocalStalk local)
    {
        if (local.OutSent || !local.Stalk)
            return;

        var stalk = local.Stalk!;
        var bud = local.Bud;

        // "is null": Unity's == also says null for a destroyed bud, which is the case handled next.
        if (bud is null)
            return;

        if (!bud)
        {
            // Picked and stored by another player while asleep here (its destroy skips the
            // bud's detach). OnDetached needs the bud, so the stalk is only withered.
            local.Bud = null;
            try
            {
                stalk.TriggerAnimatorOut(false);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Tangle: withering a stalk whose bud was picked failed: {ex.Message}");
            }
            return;
        }

        CheckLocalBud(bud);
    }

    private static void RemoveBudsOf(IntPtr stalkPointer)
    {
        if (LocalBuds.Count == 0)
            return;

        List<IntPtr>? remove = null;
        foreach (var pair in LocalBuds)
        {
            if (!pair.Value || pair.Value.Pointer == stalkPointer)
                (remove ??= new List<IntPtr>()).Add(pair.Key);
        }

        if (remove == null)
            return;

        foreach (var key in remove)
            LocalBuds.Remove(key);
    }

    /// <summary>
    /// Every 2 seconds. Repeats the stalks this game grew (for players who joined or walked in
    /// since), and removes copies whose grower stopped repeating them (left, crashed, or the session
    /// ended).
    /// </summary>
    public static void Tick()
    {
        if (!InMultiplayer)
        {
            if (RemoteStalks.Count > 0 || RemoteVines.Count > 0 || HangingBuds.Count > 0)
                ClearCopies();
            return;
        }

        var now = UnityEngine.Time.unscaledTime;

        // The host's stalks from before it opened the world to others.
        if (scanForStalks && Main.Server.IsRunning)
        {
            scanForStalks = false;
            foreach (var stalk in Object.FindObjectsOfType<VineBulbWithStalk>())
            {
                if (stalk && !IsRemoteCopy(stalk))
                    Register(stalk);
            }
        }

        if (now >= nextResend)
        {
            nextResend = now + StalkResendSeconds;

            List<IntPtr>? gone = null;
            foreach (var pair in LocalStalks)
            {
                var local = pair.Value;
                if (!local.Stalk)
                {
                    (gone ??= new List<IntPtr>()).Add(pair.Key);
                    continue;
                }

                CheckSleepingBud(local);

                if (!local.OutSent)
                    SendSpawn(local);
            }

            if (gone != null)
            {
                foreach (var key in gone)
                    LocalStalks.Remove(key);
            }
        }

        List<long>? expired = null;
        foreach (var remote in RemoteStalks.Values)
        {
            if (!remote.Stalk ||
                (!remote.Withering && now - remote.LastHeard > StalkExpireSeconds) ||
                (remote.Withering && now - remote.WitherStartedAt > WitherTimeoutSeconds))
                (expired ??= new List<long>()).Add(remote.Id);
        }

        if (expired != null)
        {
            foreach (var id in expired)
                RemoveRemoteStalk(id);
        }

        List<long>? staleBuds = null;
        foreach (var pair in HangingBuds)
        {
            if (now - pair.Value.LastHeard > StalkExpireSeconds)
                (staleBuds ??= new List<long>()).Add(pair.Value.StalkId);
        }

        if (staleBuds == null)
            return;

        foreach (var stalkId in staleBuds)
            ForgetBud(stalkId);
    }

    #endregion

    #region Stalks: copies of other players' stalks

    public static void HandleStalk(TangleStalkPacket packet)
    {
        switch (packet.Event)
        {
            case TangleStalkPacket.StalkEvent.Spawn:
                OnRemoteSpawn(packet);
                break;

            case TangleStalkPacket.StalkEvent.Out:
                EndedStalks.Add(packet.StalkId);
                ForgetBud(packet.StalkId);
                if (!RemoteStalks.TryGetValue(packet.StalkId, out var withering))
                    break;

                // While the copy still grows in, the game would ignore the wither and leave it.
                var growing = UnityEngine.Time.unscaledTime < withering.GrownAt;
                if (withering.Stalk)
                {
                    var animator = withering.Stalk!._vineBulbAnimator;
                    growing |= animator && animator!._animating;
                }

                if (!withering.Stalk || growing)
                {
                    RemoveRemoteStalk(packet.StalkId);
                    break;
                }

                withering.Withering = true;
                withering.WitherStartedAt = UnityEngine.Time.unscaledTime;
                withering.Stalk!.TriggerAnimatorOut(packet.Silent);
                break;

            case TangleStalkPacket.StalkEvent.Remove:
                EndedStalks.Add(packet.StalkId);
                ForgetBud(packet.StalkId);
                RemoveRemoteStalk(packet.StalkId);
                break;
        }
    }

    private static void OnRemoteSpawn(TangleStalkPacket packet)
    {
        if (EndedStalks.Contains(packet.StalkId))
            return;

        // Recorded whether or not a copy can be shown here, so the bud is never taken over.
        NoteBud(packet.StalkId, packet.BudActorId);

        if (RemoteStalks.TryGetValue(packet.StalkId, out var known))
        {
            if (known.Stalk)
            {
                known.LastHeard = UnityEngine.Time.unscaledTime;
                return;
            }

            RemoveRemoteStalk(packet.StalkId);
        }

        SpawnRemoteStalk(packet);
    }

    private static void SpawnRemoteStalk(TangleStalkPacket packet)
    {
        // Not while this game is loading a world, or before a joining player has the host's.
        if (NetworkActorManager.IsSceneLoadingNow)
            return;
        if (!Main.Server.IsRunning && !NetworkActorManager.InitialActorsLoaded)
            return;

        // A stalk grown in another area than the one loaded here would hang in empty space.
        var current = CurrentSceneGroupName();
        if (!string.IsNullOrEmpty(packet.SceneGroup) && !string.IsNullOrEmpty(current) && packet.SceneGroup != current)
            return;

        var prefab = FindStalkPrefab(packet.PrefabName);
        if (!prefab)
        {
            if (LoggedMisses.Add(packet.PrefabName))
                SrLogger.LogWarning($"Tangle: no stalk prefab named '{packet.PrefabName}' here, another player's bud stalk is not shown");
            return;
        }

        var copy = InstantiationHelpers.InstantiateDynamic(prefab, packet.Position, packet.Rotation);
        if (!copy)
            return;

        var stalk = copy.GetComponent<VineBulbWithStalk>();
        if (!stalk)
        {
            Object.Destroy(copy);
            return;
        }

        // Marked before the stalk's Start runs (next frame), so it is never announced as ours and
        // never grows a bud: the grower's bud reaches this game as a shared actor.
        var seedDuration = packet.ShouldSpawnSeed ? Mathf.Max(0.05f, packet.SeedDuration) : 0.05f;
        var now = UnityEngine.Time.unscaledTime;
        var remote = new RemoteStalk
        {
            Id = packet.StalkId,
            Stalk = stalk,
            LastHeard = now,
            GrownAt = now + seedDuration + StalkAnimationSeconds + 0.5f
        };
        RemoteStalkIds[stalk.Pointer] = packet.StalkId;
        RemoteStalks[packet.StalkId] = remote;

        var regionRegistry = SceneContext.Instance ? SceneContext.Instance.RegionRegistry : null;
        var sceneGroup = regionRegistry ? regionRegistry!.CurrentSceneGroup : null;
        // Always through the seed phase, briefly for a stalk shown grown: a stalk that animates in
        // from its own Start can do so before its animator's Start, which then hides the plant.
        stalk.Init(sceneGroup!, true, seedDuration, packet.IsBigSeed);

        // A stalk whose bud already hangs: the real bud is the shared actor, so the stalk's
        // stand-in bud (shown while it grows in) would be a second bud beside it.
        if (packet.BudActorId != 0 || !packet.ShouldSpawnSeed)
        {
            var dummy = stalk._dummyBudObject;
            if (dummy)
                dummy!.SetActive(false);
        }
    }

    private static void NoteBud(long stalkId, long budActorId)
    {
        if (budActorId == 0)
            return;

        if (StalkBuds.TryGetValue(stalkId, out var previous) && previous != budActorId)
            HangingBuds.Remove(previous);

        StalkBuds[stalkId] = budActorId;
        HangingBuds[budActorId] = (stalkId, UnityEngine.Time.unscaledTime);
    }

    private static void ForgetBud(long stalkId)
    {
        if (!StalkBuds.Remove(stalkId, out var budActorId))
            return;

        if (HangingBuds.TryGetValue(budActorId, out var entry) && entry.StalkId == stalkId)
            HangingBuds.Remove(budActorId);
    }

    private static void RemoveRemoteStalk(long id)
    {
        if (!RemoteStalks.Remove(id, out var remote))
            return;

        var stalk = remote.Stalk;
        if (!stalk)
            return;

        RemoteStalkIds.Remove(stalk!.Pointer);
        Object.Destroy(stalk.gameObject);
    }

    private static GameObject? FindStalkPrefab(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        if (StalkPrefabs.TryGetValue(name, out var cached) && cached)
            return cached;

        foreach (var config in Resources.FindObjectsOfTypeAll<TangleSpawnBudMetadata>())
            Remember(config ? config.VineBudWithStalkPrefab : null);

        foreach (var activity in Resources.FindObjectsOfTypeAll<SpawnTangleStalkActivity>())
        {
            var config = activity ? activity.Config : null;
            Remember(config ? config!.VineBudWithStalkPrefab : null);
        }

        if (StalkPrefabs.TryGetValue(name, out var found) && found)
            return found;

        // Any stalk prefab with that name (one not placed in a scene).
        foreach (var stalk in Resources.FindObjectsOfTypeAll<VineBulbWithStalk>())
        {
            if (!stalk)
                continue;

            var gameObject = stalk.gameObject;
            if (gameObject.name == name && !gameObject.scene.IsValid())
            {
                StalkPrefabs[name] = gameObject;
                return gameObject;
            }
        }

        return null;

        static void Remember(GameObject? prefab)
        {
            if (prefab && !string.IsNullOrEmpty(prefab!.name))
                StalkPrefabs[prefab.name] = prefab;
        }
    }

    #endregion

    #region Grab vine

    /// <summary>The tangle's vine grew and grabbed something here. If this game owns the tangle, the others see it.</summary>
    public static void OnVineGrappled(GroundVine vine)
    {
        if (!InMultiplayer || !vine)
            return;

        var networkActor = vine.GetComponent<NetworkActor>();
        if (!networkActor || !networkActor.LocallyOwned)
            return;

        var activeVine = vine._activeVine;
        if (!activeVine)
            return;

        var id = networkActor.ActorId.Value;
        var vineId = NextId();
        LocalVines[id] = vineId;

        // This game drives the tangle now; a copy left from its previous owner goes.
        DestroyRemoteVine(id);

        var transform = activeVine.transform;
        Main.SendToAllOrServer(new TangleVinePacket
        {
            ActorId = networkActor.ActorId,
            VineId = vineId,
            Phase = 1,
            Position = transform.position,
            Rotation = transform.rotation
        });
    }

    /// <summary>The vine went into another step here.</summary>
    public static void OnVinePhaseChanged(GroundVine vine, int phase)
    {
        if (!InMultiplayer || phase is not (2 or 3) || !vine)
            return;

        var networkActor = vine.GetComponent<NetworkActor>();
        if (!networkActor || !LocalVines.TryGetValue(networkActor.ActorId.Value, out var vineId))
            return;

        Main.SendToAllOrServer(new TangleVinePacket { ActorId = networkActor.ActorId, VineId = vineId, Phase = (byte)phase });
    }

    /// <summary>The vine is removed here. If this game announced it, the others' copies go too.</summary>
    public static void OnVineDestroyed(GroundVine vine)
    {
        if (!vine)
            return;

        var networkActor = vine.GetComponent<NetworkActor>();
        if (!networkActor)
            return;

        var actorId = networkActor.ActorId;
        if (!LocalVines.Remove(actorId.Value, out var vineId) || !InMultiplayer)
            return;

        Main.SendToAllOrServer(new TangleVinePacket { ActorId = actorId, VineId = vineId, Phase = 0 });
    }

    public static void HandleVine(TangleVinePacket packet)
    {
        var id = packet.ActorId.Value;

        if (packet.Phase == 0)
        {
            // Only the copy of that same firing: a late "gone" from an earlier owner leaves the
            // current one alone.
            if (RemoteVines.TryGetValue(id, out var current) && current.VineId == packet.VineId)
                DestroyRemoteVine(id);
            return;
        }

        if (!ActorManager.Actors.TryGetValue(id, out var model) || !model.TryGetNetworkComponent(out var networkActor))
            return;

        // A late packet from the tangle's previous owner: this game fires its vine now.
        if (networkActor.LocallyOwned)
            return;

        var groundVine = networkActor.GetComponent<GroundVine>();
        if (!groundVine)
            return;

        var config = groundVine._groundVineConfig;

        switch (packet.Phase)
        {
            case 1:
                SpawnRemoteVine(id, packet.VineId, groundVine, packet.Position, packet.Rotation);
                PlayCue(config ? config!.VineUpCue : null, packet.Position);
                break;

            case 2:
                if (TryGetRemoteVine(id, packet.VineId, out var pulling))
                    PlayCue(config ? config!.VineDownCue : null, pulling.transform.position);
                break;

            case 3:
                if (TryGetRemoteVine(id, packet.VineId, out var dropping))
                {
                    SetDropItem(dropping);
                    PlayCue(config ? config!.VineDropCue : null, dropping.transform.position);
                }
                break;
        }
    }

    private static bool TryGetRemoteVine(long id, long vineId, out GameObject copy)
    {
        copy = null!;
        if (!RemoteVines.TryGetValue(id, out var entry) || entry.VineId != vineId || !entry.Copy)
            return false;

        copy = entry.Copy;
        return true;
    }

    private static void SpawnRemoteVine(long id, long vineId, GroundVine groundVine, Vector3 position, Quaternion rotation)
    {
        DestroyRemoteVine(id);

        var prefab = groundVine._vinePrefab;
        if (!prefab)
        {
            if (LoggedMisses.Add("vine"))
                SrLogger.LogWarning("Tangle: this copy of the tangle has no grab vine prefab, its vine is not shown");
            return;
        }

        var copy = InstantiationHelpers.InstantiateDynamic(prefab, position, rotation);
        if (!copy)
            return;

        // Only a picture of the owner's vine: it holds nothing and pushes nothing.
        foreach (var joint in copy.GetComponentsInChildren<Joint>(true))
            Object.Destroy(joint);

        foreach (var collider in copy.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true))
            body.isKinematic = true;

        RemoteVines[id] = (copy, vineId);
        Object.Destroy(copy, RemoteVineLifetime);
    }

    private static void DestroyRemoteVine(long id)
    {
        if (RemoteVines.Remove(id, out var entry) && entry.Copy)
            Object.Destroy(entry.Copy);
    }

    private static void SetDropItem(GameObject vine)
    {
        try
        {
            var animator = vine.GetComponent<Animator>();
            if (!animator)
                animator = vine.GetComponentInChildren<Animator>();
            if (!animator)
                return;

            var id = GroundVine.DROP_ITEM;
            if (!TryGetParameterType(animator!, id, out var type))
                return;

            if (type == AnimatorControllerParameterType.Bool)
                animator!.SetBool(id, true);
            else if (type == AnimatorControllerParameterType.Trigger)
                animator!.SetTrigger(id);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle: vine drop animation failed: {ex.Message}");
        }
    }

    private static bool TryGetParameterType(Animator animator, int id, out AnimatorControllerParameterType type)
    {
        if (ParameterTypes.TryGetValue(id, out type))
            return true;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.nameHash != id)
                continue;

            type = parameter.type;
            ParameterTypes[id] = type;
            return true;
        }

        return false;
    }

    private static void PlayCue(SECTR_AudioCue? cue, Vector3 position)
    {
        if (!cue)
            return;

        try
        {
            SECTR_AudioSystem.Play(cue, position, false);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Tangle: vine sound failed: {ex.Message}");
        }
    }

    #endregion

    private static string PrefabNameOf(GameObject gameObject)
    {
        var name = gameObject.name ?? string.Empty;
        const string clone = "(Clone)";
        while (name.EndsWith(clone, StringComparison.Ordinal))
            name = name[..^clone.Length].TrimEnd();
        return name;
    }

    private static string CurrentSceneGroupName()
    {
        try
        {
            var sceneGroup = SystemContext.Instance ? SystemContext.Instance.SceneLoader.CurrentSceneGroup : null;
            return sceneGroup ? sceneGroup!.name : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // Removes every copy of other players' stalks and vines.
    private static void ClearCopies()
    {
        foreach (var remote in RemoteStalks.Values)
        {
            if (remote.Stalk)
                Object.Destroy(remote.Stalk!.gameObject);
        }

        foreach (var vine in RemoteVines.Values)
        {
            if (vine.Copy)
                Object.Destroy(vine.Copy);
        }

        RemoteStalks.Clear();
        RemoteStalkIds.Clear();
        RemoteVines.Clear();
        HangingBuds.Clear();
        StalkBuds.Clear();
    }

    /// <summary>
    /// A session started or ended: copies go, and nothing from before is matched again. The stalks
    /// this game grew stay known; they are repeated to whoever is in the new session.
    /// </summary>
    public static void ResetSession()
    {
        ClearCopies();
        EndedStalks.Clear();
        LocalVines.Clear();
        nextResend = 0f;
    }

    /// <summary>Someone joined: the standing stalks are repeated on the next tick, not up to 6 s later.</summary>
    public static void RepeatSoon() => nextResend = 0f;

    /// <summary>The host opened its world: its standing stalks are found and shown to the others.</summary>
    public static void OnServerStarted()
    {
        ResetSession();
        scanForStalks = true;
    }
}
