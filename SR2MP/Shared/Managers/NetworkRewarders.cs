using Il2CppMonomiPark.SlimeRancher.World;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Labyrinth rewards (TreasurePodRewarder) that belong to a lock or depositor another player
/// solved. When that unlock is replayed here, the game may run the reward again, possibly a while
/// later from an animation, after HandlingPacket was reset. The solver already gave it out and
/// sent it, so it is skipped here for a while.
/// </summary>
internal static class NetworkRewarders
{
    // Long enough to cover the unlock animation; the real length can't be read from here.
    private const float SuppressSeconds = 60f;

    private static readonly Dictionary<string, float> SuppressedUntil = new();

    static NetworkRewarders()
    {
        Main.Server.OnServerStarted +=      SuppressedUntil.Clear;
        Main.Client.OnConnected     += _ => SuppressedUntil.Clear();
        Main.Client.OnDisconnected  +=      SuppressedUntil.Clear;
    }

    internal static void Suppress(TreasurePodRewarder? rewarder)
    {
        try
        {
            if (!rewarder)
                return;

            var id = rewarder!._id;
            if (string.IsNullOrEmpty(id))
                return;

            SuppressedUntil[id] = UnityEngine.Time.time + SuppressSeconds;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not hold back a Labyrinth reward: {ex.Message}");
        }
    }

    internal static bool IsSuppressed(string? id)
    {
        if (string.IsNullOrEmpty(id) || !SuppressedUntil.TryGetValue(id, out var until))
            return false;

        if (UnityEngine.Time.time <= until)
            return true;

        SuppressedUntil.Remove(id);
        return false;
    }

    /// <summary>
    /// Holds back the lock's reward if the state just written will open the lock. Only then: a
    /// replayed first slot must not cost the player who fills the last slot their reward.
    /// </summary>
    internal static void SuppressIfUnlocking(PuzzleSlotLockable? lockable)
    {
        if (!lockable)
            return;

        bool unlocking;
        try
        {
            unlocking = lockable!.ShouldUnlock();
        }
        catch (Exception ex)
        {
            // Unknown: duplicate loot is the worse outcome.
            SrLogger.LogDebug($"Could not ask a Labyrinth lock whether it opens: {ex.Message}");
            unlocking = true;
        }

        if (unlocking)
            Suppress(lockable!._rewardOnUnlock);
    }
}
