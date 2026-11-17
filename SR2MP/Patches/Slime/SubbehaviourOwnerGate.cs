using SR2MP.Components.Actor;

namespace SR2MP.Patches.Slime;

// A slime's behaviour scripts (stalking, vines, yawning...) used to run on every player's copy of
// the slime, each on its own schedule. Only the player simulating the slime (its owner) runs them
// now; the other copies are frozen and get what they need to show from packets.
//
// Blocking only Selected is not enough: ownership moves while a behaviour is selected. The game's
// plexer would then call Action on a behaviour whose Selected never ran here (we just became the
// owner), or keep a half-finished behaviour around that this game no longer drives (a vine holding
// food, a stealthed tabby). So we remember which behaviours really started on this game and fix
// up both cases from Action, which the plexer calls every physics tick.
//
// This is a helper, not a patch: each behaviour type has its own small patch class that forwards
// to it, because Selected/Action/Deselected are overridden separately on every type.
internal static class SubbehaviourOwnerGate
{
    // Unity instance ids of behaviours whose Selected ran on this game and whose Deselected has
    // not run yet. Instance ids are never reused while the game runs.
    private static readonly HashSet<int> SelectedHere = new();

    // When we last tried to restart a behaviour we became the owner of, so a behaviour that keeps
    // failing to restart is not retried every physics tick.
    private static readonly Dictionary<int, float> LastRestartAttempt = new();

    private static bool loggedError;

    static SubbehaviourOwnerGate()
    {
        // A joining player's world is replaced by the host's, and leaving goes back to the menu,
        // so the remembered behaviours are gone either way. Starting a server keeps the host's
        // world (and its running behaviours) as it is, so the set is kept then.
        Main.Client.OnConnected    += _ => Clear();
        Main.Client.OnDisconnected +=      Clear;
    }

    private static void Clear()
    {
        SelectedHere.Clear();
        LastRestartAttempt.Clear();
    }

    private static bool InMultiplayer => Main.Server.IsRunning || Main.Client.IsConnected;

    private static void LogOnce(string where, Exception exception)
    {
        if (loggedError)
            return;

        loggedError = true;
        SrLogger.LogDebug($"Slime behaviour owner gate ({where}) failed, the game runs it as usual: {exception.Message}");
    }

    /// <summary>
    /// True if this game may run the behaviour: not in multiplayer, applying a packet, the slime
    /// is not a synced actor, or this game simulates the slime.
    /// </summary>
    internal static bool MayRun(Subbehaviour behaviour)
    {
        if (!InMultiplayer || HandlingPacket)
            return true;

        var networkActor = behaviour.GetComponent<NetworkActor>();
        return !networkActor || networkActor.LocallyOwned;
    }

    internal static bool SelectedPrefix(Subbehaviour behaviour)
    {
        try
        {
            return MayRun(behaviour);
        }
        catch (Exception exception)
        {
            LogOnce("Selected", exception);
            return true;
        }
    }

    // Tracked in single player too, so a host that opens their game to others mid-play does not
    // have every running behaviour look like one it never started.
    internal static void SelectedPostfix(Subbehaviour behaviour, bool ranOriginal)
    {
        try
        {
            if (ranOriginal)
                SelectedHere.Add(behaviour.GetInstanceID());
        }
        catch (Exception exception)
        {
            LogOnce("Selected postfix", exception);
        }
    }

    /// <summary>
    /// Decides whether the game's Action runs. <paramref name="afterLostOwnership"/> is extra
    /// clean-up for behaviours that hold on to other objects, run after the game's own Deselected
    /// when this game stops being the owner halfway through.
    /// </summary>
    internal static bool ActionPrefix(Subbehaviour behaviour, Action<Subbehaviour>? afterLostOwnership = null)
    {
        try
        {
            // Single player stays exactly as the game has it.
            if (!InMultiplayer)
                return true;

            var id = behaviour.GetInstanceID();

            if (MayRun(behaviour))
            {
                if (SelectedHere.Contains(id))
                    return true;

                // We just became the owner while the plexer had this behaviour selected but its
                // Selected was blocked. Let the plexer choose again so it starts properly.
                Restart(behaviour, id);
                return false;
            }

            // We started this behaviour as the owner and another player has taken the slime
            // since. Let the game end it the way it normally does (releases what it holds, ends
            // stealth), then leave it to the new owner.
            if (SelectedHere.Contains(id))
            {
                try
                {
                    behaviour.Deselected();
                }
                catch (Exception exception)
                {
                    SrLogger.LogDebug($"Ending a slime behaviour after losing ownership failed: {exception.Message}");
                }

                SelectedHere.Remove(id);

                if (afterLostOwnership != null)
                {
                    try
                    {
                        afterLostOwnership(behaviour);
                    }
                    catch (Exception exception)
                    {
                        SrLogger.LogDebug($"Slime behaviour clean-up after losing ownership failed: {exception.Message}");
                    }
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            LogOnce("Action", exception);
            return true;
        }
    }

    private static void Restart(Subbehaviour behaviour, int id)
    {
        var now = UnityEngine.Time.time;
        if (LastRestartAttempt.TryGetValue(id, out var last) && now - last < 1f)
            return;

        LastRestartAttempt[id] = now;

        try
        {
            var plexer = behaviour.Plexer;
            if (!plexer)
                return;

            plexer.ForceRethink();

            // If the plexer picked this behaviour again without calling Selected (it may skip
            // that when the choice does not change), start it ourselves so it is not stuck.
            if (SelectedHere.Contains(id))
                return;

            var current = plexer._currBehavior;
            if (current != null && current.Pointer == behaviour.Pointer)
                behaviour.Selected();
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"Restarting a slime behaviour after gaining ownership failed: {exception.Message}");
        }
    }

    internal static bool DeselectedPrefix(Subbehaviour behaviour)
    {
        try
        {
            return MayRun(behaviour) || SelectedHere.Contains(behaviour.GetInstanceID());
        }
        catch (Exception exception)
        {
            LogOnce("Deselected", exception);
            return true;
        }
    }

    internal static void DeselectedPostfix(Subbehaviour behaviour, bool ranOriginal)
    {
        try
        {
            if (!ranOriginal)
                return;

            var id = behaviour.GetInstanceID();
            SelectedHere.Remove(id);
            LastRestartAttempt.Remove(id);
        }
        catch (Exception exception)
        {
            LogOnce("Deselected postfix", exception);
        }
    }
}
