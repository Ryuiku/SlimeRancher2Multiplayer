using System.Net;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Packets.World;

namespace SR2MP.Shared.Managers;

internal static class NetworkTornadoManager
{
    private const float RenderDistance = 240f;

    internal static SpawnTornadoActivity? LastActivity;

    // Set by the cheat around its Spawn call and read by OnTornadoSpawn.Postfix: the speed the spot
    // was tested with, and the least game hours the tornado should last.
    internal static float NextSpeed = -1f;
    internal static float NextMinLifetimeHours;

    private static int currentId = 1;

    internal static readonly Dictionary<int, GameObject> HostTornados = new();
    internal static readonly Dictionary<int, GameObject> ClientTornados = new();
    internal static readonly Dictionary<int, PendingTornado> PendingTornados = new();

    // Spin (SlimenadoMoveSpiral._rot) to give a client copy once its Start has run, keyed by the mover's
    // instance id. Start always sets the spin back to its starting value, so OnTornadoMoverStart puts
    // the host's current spin back afterwards.
    internal static readonly Dictionary<int, float> PendingSpins = new();

    // Real seconds between the host's resends of every live tornado, so the others' copies (and the
    // positions of tornados they have not spawned yet) stay close to the host's.
    private const float ResendSeconds = 5f;
    private static float nextResend;

    // Host: tornados a client's game runs because the host has no colliders there (its player is not
    // near): who runs it, and the last state it reported.
    internal sealed class RelayedTornado
    {
        public IPEndPoint Owner = null!;
        public TornadoSpawnPacket? State;
        public double StateWorldTime;
        public float GrantedAt;
        public float ReportedAt;
    }

    internal static readonly Dictionary<int, RelayedTornado> RelayedTornados = new();

    // A relayed tornado whose game has not reported it for this many real seconds has ended there
    // (or that game never spawned it): it ends for everyone. Longer than the server's client timeout
    // (30 s, checked every 5 s), so a player who timed out is removed first and OnClientRemoved can
    // hand the tornado to the host.
    private const float RelayTimeoutSeconds = 45f;

    // Real seconds the host waits after its copy of a relayed tornado faded on its own before it makes a
    // new one, since the host may have loaded the ground on that path by then.
    private const float CopyRetrySeconds = 30f;
    private static readonly Dictionary<int, float> CopyFadedAt = new();

    // Client: tornados this game runs for everyone, by the host's id.
    internal static readonly Dictionary<int, GameObject> OwnedTornados = new();

    // Set around the Spawn call of a handed-over tornado; read by OnTornadoSpawn.
    internal static int NextOwnedId;

    private static float nextOwnerResend;

    internal struct PendingTornado
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Speed;
        public float Spin;
        public double FadeInWorldSeconds;
        public float ReceivedAt;
    }

    internal static int IncreaseId() => currentId++;

    internal static bool PlayerInDistance(Vector3 position)
    {
        try
        {
            var player = SceneContext.Instance?.player;
            if (player == null)
                return false;

            return (player.transform.position - position).sqrMagnitude <= RenderDistance * RenderDistance;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Brings a copy this client already has for the id in line with the host's: position, speed, spin
    /// and fade time. Returns false when there is no such copy, or it is already fading out.
    /// </summary>
    internal static bool TryUpdateExisting(int id, Vector3 position, Quaternion rotation, float speed, float spin, double fadeInWorldSeconds)
    {
        try
        {
            if (!ClientTornados.TryGetValue(id, out var existing) || !existing)
                return false;

            var vortexer = existing.GetComponent<ActorVortexer>();
            if (vortexer && vortexer!._isFading)
                return false;

            existing.transform.SetPositionAndRotation(position, rotation);

            var mover = existing.GetComponent<SlimenadoMoveSpiral>();
            if (mover)
            {
                if (speed >= 0f)
                    mover!._speed = speed;
                if (spin >= 0f)
                    mover!._rot = spin;
                mover!._tgtY = position.y;

                // Start the next ground check from the new spot, the way Start does. Otherwise the look-ahead ray
                // in SlimenadoMoveSpiral.FixedUpdate runs the whole length of the jump past the new spot and can fade the copy.
                mover!._priorGroundCheckStartPos = position + Vector3.up * mover!._groundHeightAdjustUp;
            }

            var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
            if (fadeInWorldSeconds > 0d && vortexer && time)
                vortexer!.SetFadeAtGameTime(time!.WorldTime() + fadeInWorldSeconds);

            return true;
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"TryUpdateExisting: {ex.Message}");
            return false;
        }
    }

    internal static void SpawnTornado(int id, Vector3 position, Quaternion rotation, float speed, float spin, double fadeInWorldSeconds)
    {
        // A resync sends every tornado again. Keep the copy this client already has and bring it in
        // line with the host's, instead of spawning a second one that the despawn would never reach.
        if (TryUpdateExisting(id, position, rotation, speed, spin, fadeInWorldSeconds))
            return;

        // The Prefab-checked activity first: the first one in memory can have no prefab.
        var activity = NetworkCheats.TornadoActivity() ?? EnsureActivity();
        if (activity == null)
            return;

        try
        {
            // The client's clock can still be its own save's until the initial actors arrive, so the time
            // left is counted from this client's clock. The host's despawn packet still ends it on time.
            var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;

            HandlingPacket = true;
            var tornado = activity.Spawn(position, rotation);
            if (!tornado)
                return;

            ClientTornados[id] = tornado;

            // Only the host's tornado spawns slimes. They reach everyone through the actor sync, so this
            // copy's own spawner (armed by Spawn -> PeriodicActorSpawner.Init) must not run.
            var spawner = tornado.GetComponent<PeriodicActorSpawner>();
            if (spawner)
                spawner!.enabled = false;

            // Same speed, spin and fade time as the host's copy, so both follow the same spiral and the
            // same ground and obstacle checks, and fade together.
            var mover = tornado.GetComponent<SlimenadoMoveSpiral>();
            if (speed >= 0f && mover)
                mover!._speed = speed;
            if (spin >= 0f && mover)
            {
                mover!._rot = spin;
                PendingSpins[mover.GetInstanceID()] = spin;
            }

            var vortexer = tornado.GetComponent<ActorVortexer>();
            if (fadeInWorldSeconds > 0d && vortexer && time)
                vortexer!.SetFadeAtGameTime(time!.WorldTime() + fadeInWorldSeconds);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"SpawnTornado: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    // On the host, PendingTornados only holds tornados a client's game runs; the host spawns its own
    // copy (spawner off) once its player comes near, as a client does.
    internal static void ClientTick()
    {
        if (PendingTornados.Count == 0)
            return;

        List<int>? spawnedTornados = null;
        List<int>? droppedTornados = null;
        var now = Time.unscaledTime;

        foreach (var pair in PendingTornados)
        {
            if (now - pair.Value.ReceivedAt > 90f)
                (droppedTornados ??= new List<int>()).Add(pair.Key);
            else if (PlayerInDistance(pair.Value.Position))
                (spawnedTornados ??= new List<int>()).Add(pair.Key);
        }

        if (droppedTornados != null)
            foreach (var id in droppedTornados)
                PendingTornados.Remove(id);

        if (spawnedTornados != null)
            foreach (var id in spawnedTornados)
            {
                var tornado = PendingTornados[id];
                PendingTornados.Remove(id);
                SpawnTornado(id, tornado.Position, tornado.Rotation, tornado.Speed, tornado.Spin, tornado.FadeInWorldSeconds);
            }
    }
    
    internal static void HostTick()
    {
        if (!Main.Server.IsRunning)
            return;

        ExpireRelays();
        NoteFadedCopies();

        if (HostTornados.Count == 0)
            return;

        // Gone, or fading out: the others fade their copies as soon as the host's starts to.
        List<int>? disappeared = null;
        foreach (var pair in HostTornados)
        {
            if (IsGoneOrFading(pair.Value))
                (disappeared ??= new List<int>()).Add(pair.Key);
        }

        if (disappeared != null)
        {
            foreach (var id in disappeared)
            {
                HostTornados.Remove(id);
                Main.Server.SendToAll(new TornadoSpawnPacket { TornadoId = id, Despawn = true });
            }
        }

        // Every few seconds the live ones again, so copies that drifted, and tornados a player only has
        // pending (out of range), get the host's current position and spin.
        var now = Time.unscaledTime;
        if (HostTornados.Count == 0 || now < nextResend)
            return;

        nextResend = now + ResendSeconds;
        try
        {
            foreach (var pair in HostTornados)
                Main.Server.SendToAll(CurrentState(pair.Key, pair.Value));
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"HostTick resend: {ex.Message}");
        }
    }

    internal static void SendTornadosTo(IPEndPoint client)
    {
        try
        {
            foreach (var pair in HostTornados)
            {
                // Gone or fading tornados are not sent to the new player. They stay in the list so that
                // HostTick removes them and tells the players already connected to fade their copies.
                if (IsGoneOrFading(pair.Value))
                    continue;

                Main.Server.SendToClient(CurrentState(pair.Key, pair.Value), client);
            }

            // Tornados a client's game runs, from their last report with the time left aged since then.
            var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
            foreach (var pair in RelayedTornados)
            {
                var state = pair.Value.State;
                if (state == null || pair.Value.Owner.Equals(client))
                    continue;

                var fade = state.FadeInWorldSeconds;
                if (fade > 0d && time)
                    fade = System.Math.Max(1d, fade - (time!.WorldTime() - pair.Value.StateWorldTime));

                Main.Server.SendToClient(new TornadoSpawnPacket
                {
                    TornadoId = pair.Key,
                    Position = state.Position,
                    Rotation = state.Rotation,
                    Speed = state.Speed,
                    Spin = state.Spin,
                    FadeInWorldSeconds = fade,
                }, client);
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"SendTornadosTo: {ex.Message}");
        }
    }

    // The running copy's state as it is now: position, rotation, speed, spin and the world seconds it has left.
    private static TornadoSpawnPacket CurrentState(int id, GameObject tornado)
    {
        var mover = tornado.GetComponent<SlimenadoMoveSpiral>();
        var vortexer = tornado.GetComponent<ActorVortexer>();
        var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;

        return new TornadoSpawnPacket
        {
            TornadoId = id,
            Position = tornado.transform.position,
            Rotation = tornado.transform.rotation,
            Speed = mover ? mover!._speed : -1f,
            Spin = mover ? mover!._rot : -1f,
            FadeInWorldSeconds = vortexer && time ? System.Math.Max(0d, vortexer!._fadeAtGameTime - time!.WorldTime()) : 0d,
        };
    }

    /// <summary>Forgets this client's tornado state from an earlier connection. Its copies are faded out;
    /// the host sends its live tornados again on join (SendTornadosTo).</summary>
    internal static void ResetClientState()
    {
        PendingTornados.Clear();
        PendingSpins.Clear();
        foreach (var copy in ClientTornados.Values)
        {
            try
            {
                if (copy)
                    FadeOut(copy);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"ResetClientState: {ex.Message}");
            }
        }
        ClientTornados.Clear();

        foreach (var own in OwnedTornados.Values)
        {
            try
            {
                if (own)
                    FadeOut(own);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"ResetClientState: {ex.Message}");
            }
        }
        OwnedTornados.Clear();
        NextOwnedId = 0;
    }

    // Tornados the game spawned before the server started: register and announce them like OnTornadoSpawn does.
    internal static void RegisterExisting()
    {
        // Left from an earlier session: relays and this game's copies of tornados another game ran.
        // Faded first, so the search below does not take a leftover copy for a tornado of this game.
        ForgetRelays();

        try
        {
            var known = new HashSet<int>();
            foreach (var pair in HostTornados)
                if (pair.Value)
                    known.Add(pair.Value.GetInstanceID());

            foreach (var mover in Object.FindObjectsOfType<SlimenadoMoveSpiral>())
            {
                if (!mover)
                    continue;

                var go = mover.gameObject;
                if (known.Contains(go.GetInstanceID()) || IsGoneOrFading(go))
                    continue;

                var id = IncreaseId();
                HostTornados[id] = go;
                // No one is connected yet when the server starts; SendTornadosTo and HostTick cover later joiners.
                Main.Server.SendToAll(CurrentState(id, go));
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"RegisterExisting: {ex.Message}");
        }
    }

    private static bool IsGoneOrFading(GameObject tornado)
    {
        if (!tornado)
            return true;

        var vortexer = tornado.GetComponent<ActorVortexer>();
        return vortexer && vortexer!._isFading;
    }

    /// <summary>Ends a copy the way the game ends a tornado: its vortexer fades it out and then
    /// destroys it. Without a vortexer it goes through the game's destroyer, which knows about pooling.</summary>
    internal static void FadeOut(GameObject tornado)
    {
        var vortexer = tornado.GetComponent<ActorVortexer>();
        if (vortexer)
        {
            if (!vortexer!._isFading)
                vortexer.StartFade();
            return;
        }

        Destroyer.Destroy(tornado, "SR2MP.TornadoDespawn");
    }

    // ---------------------------------------------------------------- tornados a client's game runs

    /// <summary>Host: the requesting player's game runs this tornado (the host has no colliders there).
    /// The host gives it an id and passes on what that game reports.</summary>
    internal static int GrantToRequester(IPEndPoint requester, Vector3 position, Quaternion rotation, float speed, float minHours)
    {
        var id = IncreaseId();
        RelayedTornados[id] = new RelayedTornado { Owner = requester, GrantedAt = Time.unscaledTime };
        Main.Server.SendToClient(new TornadoSpawnPacket
        {
            TornadoId = id,
            Authority = true,
            Position = position,
            Rotation = rotation,
            Speed = speed,
            MinLifetimeHours = minHours,
        }, requester);
        return id;
    }

    /// <summary>Client: the host handed this tornado to this game. A real spawn, so OnTornadoSpawn.Postfix
    /// turns its spawner on and applies the speed and lifetime, then RegisterOwned reports it.</summary>
    internal static void SpawnOwned(TornadoSpawnPacket grant)
    {
        if (Main.Server.IsRunning || OwnedTornados.ContainsKey(grant.TornadoId))
            return;

        // The Prefab-checked activity: the first one in memory can have no prefab.
        var activity = NetworkCheats.TornadoActivity() ?? EnsureActivity();
        GameObject? tornado = null;
        if (activity != null)
        {
            NextOwnedId = grant.TornadoId;
            NextSpeed = grant.Speed;
            NextMinLifetimeHours = grant.MinLifetimeHours;
            try
            {
                tornado = activity.Spawn(grant.Position, grant.Rotation);
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"SpawnOwned: {ex.Message}");
            }
            finally
            {
                NextOwnedId = 0;
                NextSpeed = -1f;
                NextMinLifetimeHours = 0f;
            }
        }

        if (!tornado || !OwnedTornados.ContainsKey(grant.TornadoId))
        {
            try
            {
                if (tornado)
                    FadeOut(tornado!);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"SpawnOwned fade: {ex.Message}");
            }
            Main.Client.SendPacket(new TornadoSpawnPacket { TornadoId = grant.TornadoId, Despawn = true });
            SrLogger.LogWarning($"Tornado {grant.TornadoId}: the host handed it to this game, but the game made no tornado");
            return;
        }

        SrLogger.LogMessage($"Tornado {grant.TornadoId}: the host is not near it, so this game runs it for everyone");
    }

    /// <summary>Client, from OnTornadoSpawn.Postfix: a handed-over tornado was spawned here.</summary>
    internal static void RegisterOwned(int id, GameObject tornado)
    {
        OwnedTornados[id] = tornado;
        var state = CurrentState(id, tornado);

        // Start has not run yet, so the others' Start gives their copies the same start value.
        state.Spin = -1f;
        Main.Client.SendPacket(state);
        nextOwnerResend = Time.unscaledTime + ResendSeconds;
    }

    /// <summary>Client: like HostTick, for the tornados this game runs for everyone.</summary>
    internal static void OwnerTick()
    {
        if (Main.Server.IsRunning || !Main.Client.IsConnected || OwnedTornados.Count == 0)
            return;

        List<int>? ended = null;
        foreach (var pair in OwnedTornados)
        {
            if (IsGoneOrFading(pair.Value) || HasStalled(pair.Value))
                (ended ??= new List<int>()).Add(pair.Key);
        }

        if (ended != null)
        {
            foreach (var id in ended)
            {
                if (OwnedTornados.Remove(id, out var go) && go)
                {
                    try { FadeOut(go); }
                    catch (Exception ex) { SrLogger.LogDebug($"OwnerTick fade: {ex.Message}"); }
                }
                Main.Client.SendPacket(new TornadoSpawnPacket { TornadoId = id, Despawn = true });
            }
        }

        var now = Time.unscaledTime;
        if (OwnedTornados.Count == 0 || now < nextOwnerResend)
            return;

        nextOwnerResend = now + ResendSeconds;
        foreach (var pair in OwnedTornados)
            Main.Client.SendPacket(CurrentState(pair.Key, pair.Value));
    }

    // Its region went to sleep here (this player left it): the game no longer moves it, and it would
    // hang still for the others.
    private static bool HasStalled(GameObject tornado)
    {
        if (!tornado.activeInHierarchy)
            return true;

        var vortexer = tornado.GetComponent<ActorVortexer>();
        var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        return vortexer && time && vortexer!._fadeAtGameTime > 0d && time!.HasReached(vortexer._fadeAtGameTime);
    }

    // A copy's state from the game that runs the tornado. Used by the clients and by the host for its
    // own copies of tornados a client's game runs.
    internal static void ApplyCopyState(TornadoSpawnPacket packet)
    {
        if (TryUpdateExisting(packet.TornadoId, packet.Position, packet.Rotation, packet.Speed, packet.Spin, packet.FadeInWorldSeconds))
        {
            PendingTornados.Remove(packet.TornadoId);
            return;
        }

        if (PlayerInDistance(packet.Position))
        {
            PendingTornados.Remove(packet.TornadoId);
            SpawnTornado(packet.TornadoId, packet.Position, packet.Rotation, packet.Speed, packet.Spin, packet.FadeInWorldSeconds);
            return;
        }

        PendingTornados[packet.TornadoId] = new PendingTornado
        {
            Position = packet.Position,
            Rotation = packet.Rotation,
            Speed = packet.Speed,
            Spin = packet.Spin,
            FadeInWorldSeconds = packet.FadeInWorldSeconds,
            ReceivedAt = Time.unscaledTime,
        };
    }

    // Fades this game's copy of the tornado the way the game ends one.
    internal static void ApplyDespawn(int id)
    {
        CopyFadedAt.Remove(id);
        PendingTornados.Remove(id);
        if (!ClientTornados.Remove(id, out var existing))
            return;

        try
        {
            if (existing)
                FadeOut(existing);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Tornado {id} despawn: {ex.Message}");
        }
    }

    /// <summary>Host: a TornadoSpawnPacket from a client. Only the game that runs a tornado may report
    /// it. True passes it on to everyone else.</summary>
    internal static bool HandleFromClient(TornadoSpawnPacket packet, IPEndPoint sender)
    {
        if (!RelayedTornados.TryGetValue(packet.TornadoId, out var relayed) || !relayed.Owner.Equals(sender))
            return false;

        packet.Authority = false;

        if (packet.Despawn)
        {
            RelayedTornados.Remove(packet.TornadoId);
            ApplyDespawn(packet.TornadoId);
            return true;
        }

        var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        relayed.State = packet;
        relayed.StateWorldTime = time ? time!.WorldTime() : 0d;
        relayed.ReportedAt = Time.unscaledTime;

        // The host's own copy faded on its own: its ground or obstacle checks failed here, so this game had
        // no colliders on that path (often because that ground was not loaded yet). A new copy right away
        // would only fade again, so the reports are just passed on until a cooldown has passed. A copy
        // destroyed under the host (its scene unloaded) is made again as soon as the host is near.
        if (ClientTornados.TryGetValue(packet.TornadoId, out var copy) && IsGoneOrFading(copy))
        {
            // HostTick notes the fade when it starts, since the copy may already be destroyed by this report.
            var now = Time.unscaledTime;
            if (copy && !CopyFadedAt.ContainsKey(packet.TornadoId))
                CopyFadedAt[packet.TornadoId] = now;
            if (CopyFadedAt.TryGetValue(packet.TornadoId, out var fadedAt) && now - fadedAt < CopyRetrySeconds)
                return true;

            // The fading copy left out of the list still finishes its fade and destroys itself.
            CopyFadedAt.Remove(packet.TornadoId);
            ClientTornados.Remove(packet.TornadoId);
        }

        // The host's own copy, once its player is near (ClientTick).
        ApplyCopyState(packet);
        return true;
    }

    // Host: notes when its copy of a relayed tornado starts to fade on its own. The copy destroys itself
    // once the fade is over, often before the owner's next report, and HandleFromClient must still know
    // it faded rather than went away with its scene.
    private static void NoteFadedCopies()
    {
        if (RelayedTornados.Count == 0 || ClientTornados.Count == 0)
            return;

        foreach (var id in RelayedTornados.Keys)
        {
            if (!CopyFadedAt.ContainsKey(id) && ClientTornados.TryGetValue(id, out var copy) && copy
                && IsGoneOrFading(copy))
                CopyFadedAt[id] = Time.unscaledTime;
        }
    }

    // Host: a relayed tornado whose game stopped reporting it (or never did) ends for everyone.
    private static void ExpireRelays()
    {
        if (RelayedTornados.Count == 0)
            return;

        var now = Time.unscaledTime;
        List<int>? stale = null;
        foreach (var pair in RelayedTornados)
        {
            var last = pair.Value.State == null ? pair.Value.GrantedAt : pair.Value.ReportedAt;
            if (now - last > RelayTimeoutSeconds)
                (stale ??= new List<int>()).Add(pair.Key);
        }

        if (stale == null)
            return;

        foreach (var id in stale)
        {
            RelayedTornados.Remove(id);
            ApplyDespawn(id);
            Main.Server.SendToAll(new TornadoSpawnPacket { TornadoId = id, Despawn = true });
            SrLogger.LogMessage($"Tornado {id}: the game running it stopped reporting it, so it ends for everyone");
        }
    }

    /// <summary>Host: a player left. The host takes over the tornados that player's game ran when it has
    /// its own copy near its player; the rest end for everyone.</summary>
    internal static void OnClientRemoved(IPEndPoint endPoint)
    {
        if (RelayedTornados.Count == 0)
            return;

        var orphaned = RelayedTornados.Where(pair => pair.Value.Owner.Equals(endPoint)).Select(pair => pair.Key).ToList();
        foreach (var id in orphaned)
        {
            RelayedTornados.Remove(id);
            PendingTornados.Remove(id);
            CopyFadedAt.Remove(id);

            if (ClientTornados.Remove(id, out var copy) && copy && !IsGoneOrFading(copy)
                && PlayerInDistance(copy.transform.position))
            {
                // The copy has the same id on every game, so HostTick's resends keep the others' copies.
                var spawner = copy.GetComponent<PeriodicActorSpawner>();
                if (spawner)
                    spawner!.enabled = true;
                HostTornados[id] = copy;
                SrLogger.LogMessage($"Tornado {id}: the player running it left, so the host runs it now");
                continue;
            }

            try
            {
                if (copy)
                    FadeOut(copy);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"OnClientRemoved: {ex.Message}");
            }
            Main.Server.SendToAll(new TornadoSpawnPacket { TornadoId = id, Despawn = true });
        }
    }

    /// <summary>Host, when the server stops: nobody runs the relayed tornados any more, so the host's
    /// copies of them fade.</summary>
    internal static void OnServerStopped() => ForgetRelays();

    private static void ForgetRelays()
    {
        RelayedTornados.Clear();
        CopyFadedAt.Clear();

        // Fades every copy this game has of a tornado another game ran, and forgets pending ones.
        ResetClientState();
    }

    private static SpawnTornadoActivity? EnsureActivity()
    {
        if (LastActivity != null)
            return LastActivity;

        try
        {
            var allTornados = Resources.FindObjectsOfTypeAll<SpawnTornadoActivity>();
            if (allTornados?.Length > 0)
                LastActivity = allTornados[0];
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"EnsureActivity: {ex.Message}");
        }

        return LastActivity;
    }
}
