using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Weather;

[HarmonyPatch(typeof(SpawnTornadoActivity), nameof(SpawnTornadoActivity.Spawn))]
internal static class OnTornadoSpawn
{
    public static bool Prefix(SpawnTornadoActivity __instance)
    {
        NetworkTornadoManager.LastActivity = __instance;
        
        // A client's own weather makes no tornados; the host's are shared. A tornado the host handed to
        // this game (NextOwnedId) is spawned for real.
        if (Main.Client.IsConnected && !Main.Server.IsRunning && !HandlingPacket && NetworkTornadoManager.NextOwnedId == 0)
            return false;

        return true;
    }

    public static void Postfix(Vector3 position, Quaternion rotation, GameObject __result)
    {
        try
        {
            if (!__result)
                return;

            var mover = __result.GetComponent<SlimenadoMoveSpiral>();
            var vortexer = __result.GetComponent<ActorVortexer>();

            // Cheat spawn: the speed its spot was tested with, and a lifetime of at least the cheat's minimum.
            if (!HandlingPacket)
            {
                // A pooled instance may have been a client copy before, with its spawner turned off
                // (NetworkTornadoManager.SpawnTornado). Init does not turn it back on.
                var ownSpawner = __result.GetComponent<Il2CppMonomiPark.SlimeRancher.World.PeriodicActorSpawner>();
                if (ownSpawner)
                    ownSpawner!.enabled = true;

                if (NetworkTornadoManager.NextSpeed >= 0f && mover)
                    mover!._speed = NetworkTornadoManager.NextSpeed;

                var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
                if (NetworkTornadoManager.NextMinLifetimeHours > 0f && vortexer && time)
                {
                    var minFadeAt = time!.HoursFromNow(NetworkTornadoManager.NextMinLifetimeHours);
                    if (vortexer!._fadeAtGameTime < minFadeAt)
                        vortexer.SetFadeAtGameTime(minFadeAt);
                }
            }

            if (HandlingPacket)
                return;

            if (!Main.Server.IsRunning)
            {
                // A client: the tornado the host handed to this game, reported from now on by OwnerTick.
                if (NetworkTornadoManager.NextOwnedId != 0)
                    NetworkTornadoManager.RegisterOwned(NetworkTornadoManager.NextOwnedId, __result);
                return;
            }

            var id = NetworkTornadoManager.IncreaseId();
            NetworkTornadoManager.HostTornados[id] = __result;

            // The speed and fade time too, so the others' copies follow the same path and fade together.
            Main.Server.SendToAll(new TornadoSpawnPacket
            {
                TornadoId = id,
                Position = position,
                Rotation = rotation,
                Speed = mover ? mover!._speed : -1f,
                // Spin stays unset: Start has not run yet, and the others' Start gives their copies the same start value.
                FadeInWorldSeconds = vortexer && SceneContext.Instance
                    ? System.Math.Max(0d, vortexer!._fadeAtGameTime - SceneContext.Instance.TimeDirector.WorldTime())
                    : 0d,
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"OnTornadoSpawn: {ex.Message}");
        }
    }
}
