using SR2MP.Packets.LandPlots;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Sends an auto-feeder's schedule (next feeding time, queued feedings and the time until its next
/// eject) after the game that runs its ranch area has fed or caught up. Before, it was only sent
/// when food came out, so feedings counted while the feeder was empty or during catch-up were never
/// reported, and the other game (and the host's save) counted them again when it took over.
/// </summary>
internal static class NetworkFeederSync
{
    private static readonly Dictionary<IntPtr, SlimeFeeder> Dirty = new();

    // What was last sent for each feeder, and when (Time.timeAsDouble).
    private static readonly Dictionary<IntPtr, (double NextFeedingTime, double NextEject, double At)> LastSent = new();

    // A feeder with no food works off its queued feedings one per physics step, and each step only
    // lowers remainingFeedOperations. After a long sleep that is a hundred or more steps, so those are
    // sent at most this often (seconds). The other game only needs the count when it takes the feeder over.
    private const double DrainSendInterval = 0.5d;

    // Only the game that runs the ranch area calls ProcessFeedOperation and UpdateToTime (the other
    // games have their FixedUpdate and catch-up blocked), and no handler calls them, so there is no echo.
    internal static void MarkDirty(SlimeFeeder feeder)
    {
        try
        {
            if (!feeder || (!Main.Server.IsRunning && !Main.Client.IsConnected))
                return;
            Dirty[feeder.Pointer] = feeder;
        }
        catch
        {
            // Never throw into the game's feeder code.
        }
    }

    /// <summary>Once a frame, after that frame's catch-up has finished: at most one packet per feeder.</summary>
    internal static void Tick()
    {
        if (Dirty.Count == 0)
            return;

        // A joining player is still in their own save.
        if (!NetworkActorManager.CanSendActorChanges)
        {
            Dirty.Clear();
            return;
        }

        var now = Time.timeAsDouble;
        foreach (var (ptr, feeder) in Dirty.ToArray())
        {
            try
            {
                if (!feeder)
                {
                    Dirty.Remove(ptr);
                    LastSent.Remove(ptr);
                    continue;
                }

                var model = feeder._model;
                var location = feeder.GetComponentInParent<LandPlotLocation>(true);
                if (model == null || !location || string.IsNullOrEmpty(location._id))
                {
                    Dirty.Remove(ptr);
                    continue;
                }

                // A new feeding cycle or food coming out is sent at once. A step that only lowered the
                // queued feedings waits for the interval and stays dirty, so the last count still goes out.
                if (LastSent.TryGetValue(ptr, out var last)
                    && last.NextFeedingTime == model.nextFeedingTime
                    && last.NextEject == feeder._nextEject
                    && now - last.At < DrainSendInterval)
                    continue;

                Dirty.Remove(ptr);
                LastSent[ptr] = (model.nextFeedingTime, feeder._nextEject, now);

                Main.SendToAllOrServer(new AutoFeederDispensePacket
                {
                    ID = location._id,
                    EjectIn = Math.Max(0d, feeder._nextEject - now),
                    NextFeedingTime = model.nextFeedingTime,
                    RemainingFeedOperations = model.remainingFeedOperations,
                });
            }
            catch (Exception ex)
            {
                Dirty.Remove(ptr);
                SrLogger.LogDebug($"Could not send a feeder schedule: {ex.Message}");
            }
        }
    }
}
