using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.ResourceAttach)]
internal sealed class ResourceAttachHandler : BasePacketHandler<ResourceAttachPacket>
{
    // Produce the game has just grown: the owner's game attaches it in the same frame it makes
    // it, but sends the new actor a frame later and on another channel, so the attach almost
    // always came first and was dropped. It waits here for its actor for a while.
    private const float PendingFor = 4f;
    private const float RetryInterval = 0.25f;
    private static readonly List<(ResourceAttachPacket Packet, float Since)> Pending = new();
    private static float nextRetry;

    protected override bool Handle(ResourceAttachPacket packet, IPEndPoint? _)
    {
        // "Not loaded on this machine" still returns true: the host passes it on to players who have it.
        if (!TryApply(packet))
        {
            Pending.RemoveAll(p => p.Packet.ActorId.Value == packet.ActorId.Value);
            Pending.Add((packet, UnityEngine.Time.time));
        }

        return true;
    }

    internal static void RetryPending()
    {
        if (Pending.Count == 0)
            return;

        var now = UnityEngine.Time.time;
        if (now < nextRetry)
            return;
        nextRetry = now + RetryInterval;

        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var (packet, since) = Pending[i];
            bool done;
            try
            {
                done = TryApply(packet) || now - since > PendingFor;
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Produce attach for actor {packet.ActorId.Value} failed: {ex.Message}");
                done = true;
            }

            if (done)
                Pending.RemoveAt(i);
        }
    }

    internal static void ClearPending() => Pending.Clear();

    /// <returns>False while the actor is not here yet.</returns>
    private static bool TryApply(ResourceAttachPacket packet)
    {
        if (!ActorManager.Actors.TryGetValue(packet.ActorId.Value, out var model))
            return false;

        var actor = model.TryCast<ActorModel>();

        if (actor == null || !actor.TryGetNetworkComponent(out var networkComponent))
            return false;

        var cycle = networkComponent.GetComponent<ResourceCycle>();
        if (cycle == null)
            return true;

        Joint? targetJoint = null;
        SpawnResource? resolvedSpawner = null;
        
        if (!string.IsNullOrEmpty(packet.PlotID)
            && GameState.landPlots.TryGetValue(packet.PlotID, out var plotModel)
            && plotModel.gameObj)
        {
            var plotSpawner = plotModel.gameObj.GetComponentInChildren<SpawnResource>();
            if (plotSpawner != null
                && packet.Joint >= 0
                && packet.Joint < plotSpawner.SpawnJoints.Count)
            {
                targetJoint     = plotSpawner.SpawnJoints[packet.Joint];
                resolvedSpawner = plotSpawner;
            }

            if (plotSpawner?._model != null)
                ApplySpawnerModel(plotSpawner._model, packet.Model);
        }
        
        if (targetJoint == null)
        {
            foreach (var spawner in Object.FindObjectsOfType<SpawnResource>())
            {
                if ((spawner.transform.position - packet.SpawnerID).sqrMagnitude >= 1f)
                    continue;

                if (packet.Joint >= 0 && packet.Joint < spawner.SpawnJoints.Count)
                {
                    targetJoint     = spawner.SpawnJoints[packet.Joint];
                    resolvedSpawner = spawner;
                }

                if (spawner._model != null)
                    ApplySpawnerModel(spawner._model, packet.Model);

                break;
            }
        }

        if (targetJoint == null || resolvedSpawner == null)
            return true;

        // Attach puts the produce back to unripe with a new timer. If the owner's state already got here
        // (produce spawned ripe, for example by the cheat menu), keep that state instead of the reset one.
        var produceModel = model.TryCast<ProduceModel>();
        var keepSynced = false;
        var syncedState = ResourceCycle.State.UNRIPE;
        var syncedProgress = 0d;
        if (produceModel != null && produceModel._state != ResourceCycle.State.UNRIPE)
        {
            keepSynced = true;
            syncedState = produceModel._state;
            syncedProgress = produceModel.progressTime;
            if (SR2MP.Components.Actor.NetworkActor.IsResourceFrozen(syncedProgress)
                && networkComponent.TryGetKnownResourceState(out _, out var knownProgress))
                syncedProgress = knownProgress;
        }

        HandlingPacket = true;
        try
        {
            cycle.Attach(targetJoint);
        }
        finally
        {
            HandlingPacket = false;
        }
        
        resolvedSpawner._spawned.Add(cycle.gameObject);

        if (produceModel != null)
        {
            if (keepSynced)
            {
                produceModel.state = syncedState;
                if (!SR2MP.Components.Actor.NetworkActor.IsResourceFrozen(syncedProgress))
                    produceModel.progressTime = syncedProgress;

                // HandleRipeState / HandleEdibleState turn the vacuumable back on and restore the full scale.
                networkComponent.SetResourceState(syncedState, syncedProgress, true);
            }
            else
            {
                networkComponent.SetResourceState(produceModel._state, produceModel.progressTime, true);

                if (produceModel._state == ResourceCycle.State.UNRIPE && cycle._defaultScale.x > 0f)
                    cycle.transform.localScale = cycle._defaultScale * 0.33f;
            }
        }

        return true;
    }

    private static void ApplySpawnerModel(SpawnResourceModel dest, SpawnResourceModel src)
    {
        dest.nextSpawnRipens        = src.nextSpawnRipens;
        dest.nextSpawnTime          = src.nextSpawnTime;
        dest.storedWater            = src.storedWater;
        dest.wasPreviouslyPlanted   = src.wasPreviouslyPlanted;
    }
}