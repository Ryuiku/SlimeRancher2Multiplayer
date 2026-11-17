using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Packets.World;

namespace SR2MP.Shared.Managers;

internal static class NetworkPlortDepositorManager
{
    private static readonly Dictionary<string, int> PendingStates = new();

    static NetworkPlortDepositorManager()
    {
        Main.Server.OnServerStarted +=      PendingStates.Clear;
        Main.Client.OnConnected     += _ => PendingStates.Clear();
        Main.Client.OnDisconnected  +=      PendingStates.Clear;
    }

    /// <summary>
    /// Plort Depositors received that have no model.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> PendingDepositorStates => PendingStates;

    private static string? ResolveId(PlortDepositor depositor, PlortDepositorModel? model)
    {
        var depositors = SceneContext.Instance.GameModel?.depositors;
        if (depositors == null)
            return null;

        var id = depositor.Id;
        if (!string.IsNullOrEmpty(id) && depositors.ContainsKey(id))
            return id;

        if (model == null)
            return null;

        foreach (var pair in depositors)
        {
            if (pair.value != null && pair.value.Pointer == model.Pointer)
                return pair.key;
        }

        return null;
    }
    
    /// <summary>
    /// Broadcasts a slot's fill state
    /// </summary>
    internal static void Broadcast(PlortDepositor depositor)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        // A joining player still has the depositors of their own save until the host's world
        // arrived. Those values must not be sent to the host.
        if (!NetworkActorManager.CanSendActorChanges)
            return;

        var model = depositor._model;
        if (model == null)
            return;

        var id = ResolveId(depositor, model);
        if (string.IsNullOrEmpty(id))
            return;

        Main.SendToAllOrServer(new PlortDepositorPacket
        {
            ID = id,
            AmountDeposited = model.AmountDeposited
        });
    }

    /// <summary>
    /// Applies a received fill amount
    /// or caches it when the depositor's model does not exist yet.
    /// </summary>
    /// <param name="exact">
    /// True for the host's full world state (join, resync), which replaces whatever we had.
    /// A deposit made during play only ever raises the amount, so two players depositing at the
    /// same moment can't undo each other.
    /// </param>
    internal static void ApplyState(string id, int amountDeposited, bool exact = false)
    {
        var depositors = SceneContext.Instance.GameModel?.depositors;
        if (depositors == null || !depositors.TryGetValue(id, out var model) || model == null)
        {
            if (exact || !PendingStates.TryGetValue(id, out var pending) || pending < amountDeposited)
                PendingStates[id] = amountDeposited;

            // Also written to the world, so a player who never loads that area (often the host) saves it.
            try
            {
                var created = depositors != null ? GameState.InitializePlortDepositor(id) : null;
                if (created != null && (exact || created.AmountDeposited < amountDeposited))
                    created.AmountDeposited = amountDeposited;
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Could not record plort depositor {id}: {ex.Message}");
            }

            return;
        }

        PendingStates.Remove(id);

        if (model.AmountDeposited == amountDeposited)
            return;

        if (!exact && model.AmountDeposited > amountDeposited)
            return;

        model.AmountDeposited = amountDeposited;

        // The depositor may not be loaded on this machine (its zone isn't loaded), and
        // NotifyParticipants throws without a game object. The game reads the model when the zone loads.
        if (!model._gameObject)
            return;

        // The player who deposited already gave out the rewards this unlocks. Before
        // NotifyParticipants, which may already start the unlock.
        var loadedDepositor = model._gameObject.GetComponent<PlortDepositor>();
        if (loadedDepositor)
            SuppressReplayedRewards(loadedDepositor!, amountDeposited);

        // Each step on its own: one of them failing must not leave the door shut.
        try
        {
            model.NotifyParticipants();
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Plort depositor {id}: NotifyParticipants failed: {ex.Message}");
        }

        var depositor = model._gameObject.GetComponent<PlortDepositor>();
        if (!depositor)
            return;

        try
        {
            depositor.OnFilledChangedFromModel();
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Plort depositor {id}: OnFilledChangedFromModel failed: {ex.Message}");
        }

        try
        {
            // the lock only re-evaluates when told to,
            // it requires an extra invitation
            depositor._puzLockable?.NotifySlotChanged(false);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Plort depositor {id}: NotifySlotChanged failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a pending state to a depositor that just got a model.
    /// </summary>
    internal static void ApplyPendingState(PlortDepositor depositor, PlortDepositorModel? model)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;
        if (model == null || PendingStates.Count == 0) return;

        var id = ResolveId(depositor, model);
        if (id == null || !PendingStates.Remove(id, out var amountDeposited))
            return;

        if (model.AmountDeposited == amountDeposited)
            return;

        HandlingPacket = true;
        try
        {
            model.AmountDeposited = amountDeposited;
            SuppressReplayedRewards(depositor, amountDeposited);
            depositor.OnFilledChangedFromModel();
            model.NotifyParticipants();
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    private static void SuppressReplayedRewards(PlortDepositor depositor, int amountDeposited)
    {
        try
        {
            if (amountDeposited >= depositor.FillAmount)
                NetworkRewarders.Suppress(depositor._rewarder);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not check plort depositor reward: {ex.Message}");
        }

        NetworkRewarders.SuppressIfUnlocking(depositor._puzLockable);
    }
}
