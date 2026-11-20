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

        var activity = EnsureActivity();
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

    internal static void ClientTick()
    {
        if (Main.Server.IsRunning || PendingTornados.Count == 0)
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
        if (!Main.Server.IsRunning || HostTornados.Count == 0)
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
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"SendTornadosTo: {ex.Message}");
        }
    }

    // The host copy's state as it is now: position, rotation, speed, spin and the world seconds it has left.
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
    }

    // Tornados the game spawned before the server started: register and announce them like OnTornadoSpawn does.
    internal static void RegisterExisting()
    {
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
