using System.Collections;
using Il2CppMonomiPark.SlimeRancher.Caretaker;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Player;
using SR2MP.Packets.Sanctuary;
using SR2MP.Shared.Utils;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Sprinkle canisters. The game fills them on every machine that has the Sanctuary loaded, from
/// each machine's own clock, so two players there doubled the output and a player arriving later
/// added the whole backlog again. Here one player in the Sanctuary produces (the lowest player id
/// among those there, worked out the same way on every machine) and sends the canister's state;
/// everyone else only shows it, and writes it to their world when the Sanctuary is not loaded.
/// </summary>
internal static class NetworkSprinkleCanisterManager
{
    internal const string AmmoKeyPrefix = "sprinkle_";

    // States for canisters that have no model here yet, by canister id.
    private static readonly Dictionary<string, SprinkleCanisterState> Pending = new();

    // When each canister's state was last sent by this game, and which ones changed since.
    private static readonly Dictionary<string, float> LastSent = new();
    private static readonly HashSet<string> Dirty = new();
    private const float SendInterval = 5f;

    private static int producerFrame = -1;
    private static bool producerCached;
    private static bool? lastLoggedProducer;

    static NetworkSprinkleCanisterManager()
    {
        Main.Server.OnServerStarted +=      ClearSession;
        Main.Client.OnConnected     += _ => ClearSession();
        Main.Client.OnDisconnected  +=      ClearSession;
    }

    private static void ClearSession()
    {
        Pending.Clear();
        LastSent.Clear();
        Dirty.Clear();
        producerFrame = -1;
        lastLoggedProducer = null;
    }

    internal static bool InSession => Main.Server.IsRunning || Main.Client.IsConnected;

    #region Ids

    internal static string? CanisterId(SprinkleCanister canister)
    {
        try
        {
            var id = canister._model?.Id;
            if (!string.IsNullOrEmpty(id))
                return id;
        }
        catch
        {
            // no model yet
        }

        try
        {
            var id = canister._idHandler ? canister._idHandler.Id : null;
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The storage key the ammo packets use. Built from the canister's id, which comes from the
    /// scene, so it is the same on every machine.
    /// </summary>
    internal static string? AmmoKey(SprinkleCanister canister)
    {
        var id = CanisterId(canister);
        return id == null ? null : AmmoKeyPrefix + id;
    }

    internal static void MarkDirtyByAmmoKey(string? ammoKey)
    {
        if (ammoKey == null || !ammoKey.StartsWith(AmmoKeyPrefix, StringComparison.Ordinal))
            return;

        Dirty.Add(ammoKey.Substring(AmmoKeyPrefix.Length));
    }

    internal static void MarkDirty(string? canisterId)
    {
        if (!string.IsNullOrEmpty(canisterId))
            Dirty.Add(canisterId!);
    }

    private static SprinkleCanisterModel? FindModel(string id)
    {
        var models = GameState?._sprinkleCanisterModels;
        if (models == null)
            return null;

        if (models.TryGetValue(id, out var model) && model != null)
            return model;

        foreach (var pair in models)
        {
            if (pair.Value != null && pair.Value.Id == id)
                return pair.Value;
        }

        return null;
    }

    #endregion

    #region Producer

    /// <summary>
    /// Whether this game makes the sprinkles. Only asked by canisters that are loaded here, so this
    /// player is in the Sanctuary.
    /// </summary>
    internal static bool IsLocalProducer()
    {
        if (!InSession)
            return true;

        // A joining player is still in their own save.
        if (!Main.Server.IsRunning && !NetworkActorManager.InitialActorsLoaded)
            return false;

        if (producerFrame == UnityEngine.Time.frameCount)
            return producerCached;

        producerFrame = UnityEngine.Time.frameCount;

        var localId = LocalID;
        var present = new List<string> { localId };

        try
        {
            var sanctuaryId = SanctuaryGroupId();
            if (sanctuaryId >= 0)
            {
                foreach (var player in PlayerManager.GetAllPlayers())
                {
                    if (player == null || string.IsNullOrEmpty(player.PlayerId) || player.PlayerId == localId)
                        continue;

                    if (player.SceneGroup == sanctuaryId)
                        present.Add(player.PlayerId);
                }
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not list the players in the Sanctuary: {ex.Message}");
        }

        present.Sort(string.CompareOrdinal);
        producerCached = present[0] == localId;

        if (lastLoggedProducer != producerCached)
        {
            lastLoggedProducer = producerCached;
            SrLogger.LogMessage(producerCached
                ? $"This game now produces sprinkles; players here: {string.Join(", ", present)}"
                : $"{present[0]} now produces sprinkles; players here: {string.Join(", ", present)}");
        }

        return producerCached;
    }

    private static int SanctuaryGroupId()
    {
        try
        {
            var group = SceneContext.Instance?.SanctuaryRoomDirector?._sanctuarySceneGroup;
            if (group != null)
            {
                var id = NetworkSceneManager.GetPersistentID(group);
                if (id >= 0)
                    return id;
            }
        }
        catch
        {
            // fall back to where this player is
        }

        var current = SystemContext.Instance?.SceneLoader?._currentSceneGroup;
        return current != null ? NetworkSceneManager.GetPersistentID(current) : -1;
    }

    #endregion

    #region Capture and apply

    internal static SprinkleCanisterState Capture(SprinkleCanisterModel model, AmmoSlotManager? liveAmmo)
    {
        var state = new SprinkleCanisterState
        {
            Id = model.Id ?? string.Empty,
            LastUpdateTime = model.LastUpdateTime,
            CurrentRate = model.CurrentRate,
            AccumulatedSprinkles = model.AccumulatedSprinkles
        };

        var slots = liveAmmo?.Slots ?? model.Ammo?.Slots;
        if (slots != null)
        {
            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    state.Slots.Add((-1, 0));
                    continue;
                }

                var count = slot._count;
                var ident = slot._id && count > 0 ? NetworkActorManager.GetPersistentID(slot._id) : -1;
                state.Slots.Add((ident, ident < 0 ? 0 : count));
            }
        }

        return state;
    }

    /// <summary>
    /// Writes a canister state from the network. When the canister is built here, only the
    /// production values are written (the add and remove packets keep its contents) unless
    /// <paramref name="withContents"/> asks for everything, as the host's world on join does.
    /// </summary>
    internal static void Apply(SprinkleCanisterState state, bool withContents = false)
    {
        if (string.IsNullOrEmpty(state.Id))
            return;

        HandlingPacket = true;
        try
        {
            var model = FindModel(state.Id) ?? TryCreateModel(state);
            if (model == null)
            {
                Pending[state.Id] = state;
                return;
            }

            Pending.Remove(state.Id);
            WriteScalars(model, state);

            var live = NetworkAmmoManager.GetAmmo(AmmoKeyPrefix + state.Id);
            if (live == null)
            {
                WriteSlots(model.Ammo?.Slots, state.Slots, false);
            }
            else if (withContents)
            {
                WriteSlots(live.Slots, state.Slots, true);
                WriteSlots(model.Ammo?.Slots, state.Slots, false);
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply sprinkle canister {state.Id}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    /// <summary>
    /// A canister this world has no record of (this player, often the host, has never loaded the
    /// Sanctuary) is recorded from the producer's state, the way the game would save it. Kept only
    /// in memory it was lost when the host restarted, and the next join then deleted the producer's
    /// own canister because the host had none. Null when it cannot be made; the state then waits.
    /// </summary>
    private static SprinkleCanisterModel? TryCreateModel(SprinkleCanisterState state)
    {
        // Without its slots the canister could not be saved or built later; it waits instead.
        if (state.Slots.Count == 0)
            return null;

        try
        {
            var gameModel = GameState;
            if (gameModel?._sprinkleCanisterModels == null)
                return null;

            var model = gameModel.InitializeSprinkleCanisterModel(state.Id, state.LastUpdateTime);
            if (model == null)
                return null;

            // Like a canister loaded from a save: each slot holds only its contents; the canister
            // sets the rest up from its own definitions when it is built.
            if (model.Ammo == null || model.Ammo.Slots == null || model.Ammo.Slots.Length < state.Slots.Count)
            {
                var slots = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AmmoSlot>(state.Slots.Count);
                for (var i = 0; i < slots.Length; i++)
                    slots[i] = new AmmoSlot();

                model.Ammo ??= new AmmoModel(null);
                model.Ammo.Slots = slots;
            }

            SrLogger.LogMessage($"Sprinkle canister {state.Id}: recorded in this world from the producer's state");
            return model;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not record sprinkle canister {state.Id}: {ex.Message}");
            return null;
        }
    }

    private static void WriteScalars(SprinkleCanisterModel model, SprinkleCanisterState state)
    {
        model.LastUpdateTime = state.LastUpdateTime;
        model.CurrentRate = state.CurrentRate;
        model.AccumulatedSprinkles = state.AccumulatedSprinkles;
    }

    private static void WriteSlots(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AmmoSlot>? slots,
        List<(int Ident, int Count)> values, bool live)
    {
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length && i < values.Count; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            var (identId, count) = values[i];

            IdentifiableType? ident = null;
            if (identId >= 0 && count > 0)
                ActorManager.ActorTypes.TryGetValue(identId, out ident);

            if (ident == null)
                count = 0;

            if (slot._count == count && (count == 0 || slot._id == ident))
                continue;

            slot._id = ident;

            // A built canister is told through the setter, so its pips and display follow.
            if (live)
                slot.Count = count;
            else
                slot._count = count;
        }
    }

    #endregion

    #region Canister life

    /// <summary>
    /// A canister is being given its model while the Sanctuary loads. A state that arrived before
    /// it had a model goes in now, so its catch-up starts from the shared clock.
    /// </summary>
    internal static void BeforeSetModel(SprinkleCanisterModel model)
    {
        if (!InSession || Pending.Count == 0)
            return;

        var id = model.Id;
        if (string.IsNullOrEmpty(id) || !Pending.Remove(id, out var state))
            return;

        HandlingPacket = true;
        try
        {
            WriteScalars(model, state);
            WriteSlots(model.Ammo?.Slots, state.Slots, false);
            SrLogger.LogMessage($"Sprinkle canister {id}: state from another player applied as it loads");
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    internal static void AfterSetModel(SprinkleCanister canister)
    {
        StartCoroutine(RegisterLater(canister));
    }

    private static IEnumerator RegisterLater(SprinkleCanister canister)
    {
        yield return new WaitFrames(3);

        try
        {
            if (canister == null || !canister)
                yield break;

            var key = AmmoKey(canister);
            var ammo = canister.GetRelevantAmmo();
            if (key == null || ammo == null)
            {
                SrLogger.LogWarning($"Sprinkle canister {canister.name} could not be registered for sync yet");
                yield break;
            }

            ammo.RegisterAmmoPointer(key);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not register a sprinkle canister: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends the canister's state now and then while this game produces, and right after it changed.
    /// </summary>
    internal static void AfterTick(SprinkleCanister canister)
    {
        if (!InSession || HandlingPacket || !NetworkActorManager.CanSendActorChanges)
            return;

        var model = canister._model;
        if (model == null || string.IsNullOrEmpty(model.Id))
            return;

        var id = model.Id;
        var now = UnityEngine.Time.realtimeSinceStartup;
        var due = !LastSent.TryGetValue(id, out var last) || now - last >= SendInterval;

        if (!due && !Dirty.Contains(id))
            return;

        if (!IsLocalProducer())
            return;

        LastSent[id] = now;
        Dirty.Remove(id);

        Main.SendToAllOrServer(new SprinkleCanisterPacket
        {
            State = Capture(model, NetworkAmmoManager.GetAmmo(AmmoKeyPrefix + id))
        });
    }

    #endregion

    #region Join snapshot

    internal static InitialSprinkleCanistersPacket CreateInitialPacket()
    {
        var packet = new InitialSprinkleCanistersPacket();
        var seen = new HashSet<string>();

        var models = GameState?.AllSprinkleCanisters();
        if (models != null)
        {
            foreach (var pair in models)
            {
                var model = pair.Value;
                if (model == null || string.IsNullOrEmpty(model.Id))
                    continue;

                try
                {
                    packet.Canisters.Add(Capture(model, NetworkAmmoManager.GetAmmo(AmmoKeyPrefix + model.Id)));
                    seen.Add(model.Id);
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Could not read sprinkle canister {model.Id}: {ex.Message}");
                }
            }
        }

        foreach (var (id, state) in Pending)
        {
            if (seen.Add(id))
                packet.Canisters.Add(state);
        }

        return packet;
    }

    internal static void ApplyInitialPacket(InitialSprinkleCanistersPacket packet)
    {
        var hostIds = new HashSet<string>(packet.Canisters.Select(c => c.Id));
        HostWorldState.DropEntriesHostLacks(GameState?._sprinkleCanisterModels, hostIds,
            // Kept: canisters built right now, and any whose own id the host has (in case the
            // world's key for a canister is not its id).
            m => (m._gameObj != null && m._gameObj) || (m.Id != null && hostIds.Contains(m.Id)),
            "sprinkle canisters");

        foreach (var state in packet.Canisters)
        {
            try
            {
                Apply(state, true);
            }
            catch (Exception ex)
            {
                HandlingPacket = false;
                SrLogger.LogWarning($"Could not apply sprinkle canister {state.Id}: {ex.Message}");
            }
        }

        SrLogger.LogMessage($"Applied the host's sprinkle canisters: {packet.Canisters.Count}.");
    }

    #endregion
}
