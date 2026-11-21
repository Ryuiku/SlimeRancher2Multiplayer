using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.UI.Themes;
using System.Net;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Packets.Sanctuary;
using SR2MP.Shared.Utils;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Sanctuary rooms: which theme each room shows, and which gadget stands in each of its pre-placed
/// spots. The game keeps both in each player's own room models; here the host's world is the
/// source of truth and changes by anyone are shared.
/// </summary>
internal static class NetworkSanctuaryManager
{
    // Rooms that are built right now, by room id.
    private static readonly Dictionary<string, SanctuaryRoomController> LiveRooms = new();

    // Themes for rooms that have no model on this machine yet, by room id.
    private static readonly Dictionary<string, string> PendingThemes = new();

    // Pre-placed gadgets for rooms that have no model on this machine yet.
    private static readonly Dictionary<string, Dictionary<string, long>> PendingPreplaced = new();

    // The theme each room was last told to show, sent or received. A room's theme can be written
    // through the room and through its model; this keeps one change from being sent twice.
    private static readonly Dictionary<string, string> LastKnownThemes = new();

    // Host only: when each pre-placed spot was last given a gadget, by room and spot (real time).
    // A gadget picked moments ago may simply not have reached the host's world yet.
    private static readonly Dictionary<(string Room, string Spot), float> PreplacedSetAt = new();
    private const float PreplacedGrace = 30f;

    static NetworkSanctuaryManager()
    {
        Main.Server.OnServerStarted +=      ClearSession;
        Main.Client.OnConnected     += _ => ClearSession();
        Main.Client.OnDisconnected  +=      ClearSession;
    }

    private static void ClearSession()
    {
        PendingThemes.Clear();
        PendingPreplaced.Clear();
        LastKnownThemes.Clear();
        PreplacedSetAt.Clear();
    }

    internal static IReadOnlyDictionary<string, string> PendingThemeEntries => PendingThemes;

    #region Room lookup

    internal static void RegisterRoom(SanctuaryRoomController controller, SanctuaryRoomModel? model)
    {
        var id = ResolveRoomId(controller, model);
        if (!string.IsNullOrEmpty(id))
            LiveRooms[id!] = controller;
    }

    private static SanctuaryRoomController? GetLiveRoom(string roomId)
    {
        if (!LiveRooms.TryGetValue(roomId, out var controller))
            return null;

        if (controller != null && controller)
            return controller;

        LiveRooms.Remove(roomId);
        return null;
    }

    internal static bool IsRoomLive(SanctuaryRoomModel model)
    {
        foreach (var (id, controller) in LiveRooms.ToArray())
        {
            if (controller == null || !controller)
            {
                LiveRooms.Remove(id);
                continue;
            }

            try
            {
                var live = controller._model;
                if (live != null && live.Pointer == model.Pointer)
                    return true;
            }
            catch
            {
                // a room that is being torn down
            }
        }

        return false;
    }

    /// <summary>
    /// The key the world uses for this room. Matched by model first, the same way puzzle slots
    /// are, because the room's own id and the world's key are not proven to be the same.
    /// </summary>
    internal static string? ResolveRoomId(SanctuaryRoomController controller, SanctuaryRoomModel? model = null)
    {
        model ??= controller._model;

        var rooms = SceneContext.Instance?.GameModel?.sanctuaryRooms;
        if (rooms != null && model != null)
        {
            foreach (var pair in rooms)
            {
                if (pair.Value != null && pair.Value.Pointer == model.Pointer)
                    return pair.Key;
            }
        }

        var id = controller.Id;
        return string.IsNullOrEmpty(id) ? null : id;
    }

    internal static string? ResolveRoomId(SanctuaryRoomModel model)
    {
        var rooms = SceneContext.Instance?.GameModel?.sanctuaryRooms;
        if (rooms == null)
            return null;

        foreach (var pair in rooms)
        {
            if (pair.Value != null && pair.Value.Pointer == model.Pointer)
                return pair.Key;
        }

        return null;
    }

    #endregion

    #region Sending

    private static bool CanSend =>
        !HandlingPacket && NetworkActorManager.CanSendActorChanges && !NetworkActorManager.IsSceneLoadingNow;

    /// <summary>
    /// A player here picked a new theme for a room.
    /// </summary>
    internal static void ReportThemeChange(string? roomId, string? oldRefId, string? newRefId)
    {
        if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(newRefId) || oldRefId == newRefId)
            return;

        if (!CanSend)
            return;

        if (LastKnownThemes.TryGetValue(roomId!, out var last) && last == newRefId)
            return;

        LastKnownThemes[roomId!] = newRefId!;

        Main.SendToAllOrServer(new SanctuaryRoomPacket
        {
            RoomId = roomId!,
            ThemeRefId = newRefId!
        });

        SrLogger.LogMessage($"Sanctuary room {roomId}: theme {newRefId} sent");
    }

    /// <summary>
    /// The game put a gadget in a room's pre-placed spot here (usually the first time anyone
    /// enters the room). Shared so the others use that gadget instead of building their own.
    /// </summary>
    internal static void ReportPreplacedGadget(SanctuaryRoomModel model, string? gadgetId, long actorId)
    {
        if (string.IsNullOrEmpty(gadgetId) || actorId == 0)
            return;

        // Not gated on zone loads: the game fills these spots while the room loads.
        if (HandlingPacket || !NetworkActorManager.CanSendActorChanges)
            return;

        var roomId = ResolveRoomId(model);
        if (string.IsNullOrEmpty(roomId))
            return;

        // The host's own pick always stands; what a client picks is checked against it.
        if (Main.Server.IsRunning)
            PreplacedSetAt[(roomId!, gadgetId!)] = UnityEngine.Time.realtimeSinceStartup;

        Main.SendToAllOrServer(new SanctuaryRoomPacket
        {
            RoomId = roomId!,
            PreplacedGadgets = { (gadgetId!, actorId) }
        });

        SrLogger.LogMessage($"Sanctuary room {roomId}: pre-placed gadget {gadgetId} is actor {actorId}, sent");
    }

    #endregion

    #region Applying

    /// <summary>
    /// Shows a theme another player picked: rebuilt live when the room is built here, otherwise
    /// written to the room's model, otherwise kept until the room gets a model.
    /// </summary>
    internal static void ApplyTheme(string roomId, string refId)
    {
        if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(refId))
            return;

        var theme = UnlockLookup.FindTheme(refId);
        if (!theme)
        {
            SrLogger.LogWarning($"Sanctuary room {roomId}: theme {refId} is unknown here, skipped");
            return;
        }

        LastKnownThemes[roomId] = refId;

        HandlingPacket = true;
        try
        {
            var controller = GetLiveRoom(roomId);
            if (controller != null)
            {
                if (controller.GetTheme()?.ReferenceId == refId)
                    return;

                controller.SetTheme(theme);
                PendingThemes.Remove(roomId);
                SrLogger.LogMessage($"Sanctuary room {roomId}: theme {refId} applied live");
                return;
            }

            var rooms = GameState?.sanctuaryRooms;
            if (rooms != null && rooms.TryGetValue(roomId, out var model) && model != null)
            {
                PendingThemes.Remove(roomId);
                if (model.GetTheme()?.ReferenceId == refId)
                    return;

                model.SetTheme(theme);
                SrLogger.LogMessage($"Sanctuary room {roomId}: theme {refId} applied to the model");
                return;
            }

            // Kept for when the room loads here, and written to the world as well: a player who
            // never goes to the Sanctuary (often the host) otherwise never saves it.
            PendingThemes[roomId] = refId;

            var created = TryCreateModel(roomId);
            if (created != null)
            {
                created.SetTheme(theme);
                SrLogger.LogMessage($"Sanctuary room {roomId}: theme {refId} recorded in a new model");
            }
            else
            {
                SrLogger.LogMessage($"Sanctuary room {roomId}: theme {refId} pending until the room loads");
            }
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    /// <summary>
    /// Host: a client's pick for pre-placed spots. Two games entering a room at about the same
    /// time each filled an empty spot with a gadget of their own, and each then recorded the
    /// other's, so both gadgets stayed and the saves disagreed which one belongs to the spot.
    /// The host keeps the gadget it already has for a spot; a pick for it is taken out of the
    /// packet (so it is not passed on) and the sender is told the gadget that stands there. The
    /// sender then removes its own gadget. A pick replaces the host's only when the host's gadget
    /// is gone from its world and was not given to the spot just now.
    /// </summary>
    internal static void ArbitratePreplaced(SanctuaryRoomPacket packet, IPEndPoint? sender)
    {
        if (string.IsNullOrEmpty(packet.RoomId) || packet.PreplacedGadgets.Count == 0)
            return;

        SanctuaryRoomModel? model = null;
        try
        {
            GameState?.sanctuaryRooms?.TryGetValue(packet.RoomId, out model);
        }
        catch
        {
            model = null;
        }

        var now = UnityEngine.Time.realtimeSinceStartup;
        List<(string GadgetId, long ActorId)>? standing = null;

        for (var i = packet.PreplacedGadgets.Count - 1; i >= 0; i--)
        {
            var (gadgetId, actorId) = packet.PreplacedGadgets[i];
            if (string.IsNullOrEmpty(gadgetId) || actorId == 0)
                continue;

            long current = 0;
            try
            {
                if (model != null && model.TryGetPreplacedGadget(new SanctuaryRoomPreplacedGadgetId(gadgetId), out var found))
                    current = found.Value;
            }
            catch
            {
                current = 0;
            }

            if (current == 0 || current == actorId || HostGadgetIsGone(packet.RoomId, gadgetId, current, now))
            {
                PreplacedSetAt[(packet.RoomId, gadgetId)] = now;
                continue;
            }

            packet.PreplacedGadgets.RemoveAt(i);
            (standing ??= new()).Add((gadgetId, current));
            SrLogger.LogMessage($"Sanctuary room {packet.RoomId}: pre-placed gadget {gadgetId} already is actor {current}; a player's actor {actorId} was turned down");
        }

        if (standing != null && sender != null)
            Main.Server.SendToClient(new SanctuaryRoomPacket { RoomId = packet.RoomId, PreplacedGadgets = standing }, sender);
    }

    private static bool HostGadgetIsGone(string roomId, string gadgetId, long actorId, float now)
    {
        if (PreplacedSetAt.TryGetValue((roomId, gadgetId), out var setAt) && now - setAt < PreplacedGrace)
            return false;

        try
        {
            return !GameState.TryGetIdentifiableModel(new ActorId(actorId), out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes pre-placed gadgets from the network. <paramref name="fromHostLive"/>: a client
    /// applying what the host decided during play; a gadget of this game's own that lost its spot
    /// is then removed for everyone, so no second copy stays in the room.
    /// </summary>
    internal static void ApplyPreplaced(string roomId, List<(string GadgetId, long ActorId)> entries, bool fromHostLive = false)
    {
        if (string.IsNullOrEmpty(roomId) || entries.Count == 0)
            return;

        var rooms = GameState?.sanctuaryRooms;
        SanctuaryRoomModel? model = null;
        if (rooms == null || !rooms.TryGetValue(roomId, out model) || model == null)
            model = TryCreateModel(roomId);

        if (model == null)
        {
            if (!PendingPreplaced.TryGetValue(roomId, out var pending))
                PendingPreplaced[roomId] = pending = new Dictionary<string, long>();

            foreach (var (gadgetId, actorId) in entries)
                pending[gadgetId] = actorId;

            SrLogger.LogMessage($"Sanctuary room {roomId}: {entries.Count} pre-placed gadgets pending until the room loads");
            return;
        }

        var lost = WritePreplaced(roomId, model, entries, fromHostLive && !Main.Server.IsRunning);
        if (lost == null)
            return;

        foreach (var actorId in lost)
            RemoveLostGadget(roomId, actorId);
    }

    private static List<long>? WritePreplaced(string roomId, SanctuaryRoomModel model,
        IEnumerable<(string GadgetId, long ActorId)> entries, bool collectLost = false)
    {
        List<long>? lost = null;

        HandlingPacket = true;
        try
        {
            foreach (var (gadgetId, actorId) in entries)
            {
                if (string.IsNullOrEmpty(gadgetId) || actorId == 0)
                    continue;

                try
                {
                    var key = new SanctuaryRoomPreplacedGadgetId(gadgetId);
                    if (model.TryGetPreplacedGadget(key, out var current))
                    {
                        if (current.Value == actorId)
                            continue;

                        if (collectLost && IsOwnActorId(current.Value))
                            (lost ??= new()).Add(current.Value);
                    }

                    model.SetPreplacedGadget(key, new ActorId(actorId));
                    SrLogger.LogMessage($"Sanctuary room {roomId}: pre-placed gadget {gadgetId} is actor {actorId}");
                }
                catch (Exception ex)
                {
                    SrLogger.LogWarning($"Sanctuary room {roomId}: could not set pre-placed gadget {gadgetId}: {ex.Message}");
                }
            }
        }
        finally
        {
            HandlingPacket = false;
        }

        return lost;
    }

    // An id this game handed out in this session (each player has its own range of ids). Ids of
    // this player's own save before joining are in the host's range and never count.
    private static bool IsOwnActorId(long actorId)
    {
        try
        {
            var provider = GameState?._actorIdProvider;
            if (provider == null)
                return false;

            var range = provider._nextActorId / ActorIdOffset;
            return range != 0 && actorId / ActorIdOffset == range;
        }
        catch
        {
            return false;
        }
    }

    // Our gadget lost its pre-placed spot to the gadget the host kept: removed the usual way, so
    // the removal reaches everyone who got our gadget.
    private static void RemoveLostGadget(string roomId, long actorId)
    {
        try
        {
            if (!GameState.TryGetIdentifiableModel(new ActorId(actorId), out var actor) || actor == null)
                return;

            GameObject? obj = null;
            try { obj = actor.GetGameObject(); } catch { /* not built */ }

            if (obj == null || !obj)
            {
                SrLogger.LogMessage($"Sanctuary room {roomId}: our gadget {actorId} lost its pre-placed spot but is not built here; left as it is");
                return;
            }

            Destroyer.DestroyGadget(obj, "SR2MP.PreplacedGadgetLost");
            SrLogger.LogMessage($"Sanctuary room {roomId}: removed our gadget {actorId}, which lost its pre-placed spot");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Sanctuary room {roomId}: could not remove our gadget {actorId}: {ex.Message}");
        }
    }

    private static SanctuaryRoomModel? TryCreateModel(string roomId)
    {
        try
        {
            return GameState?.sanctuaryRooms != null ? GameState.InitializeSanctuaryRoomModel(roomId) : null;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not record sanctuary room {roomId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// A room is being given its model while its zone loads. What arrived for it before it had
    /// one goes into the model now, so the room builds the right theme itself, with no transition.
    /// </summary>
    internal static void ApplyPendingTo(SanctuaryRoomController controller, SanctuaryRoomModel model)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        if (PendingThemes.Count == 0 && PendingPreplaced.Count == 0)
            return;

        var roomId = ResolveRoomId(controller, model);
        if (string.IsNullOrEmpty(roomId))
            return;

        if (PendingThemes.Remove(roomId!, out var refId))
        {
            var theme = UnlockLookup.FindTheme(refId);
            if (theme && model.GetTheme()?.ReferenceId != refId)
            {
                HandlingPacket = true;
                try
                {
                    model.SetTheme(theme);
                }
                finally
                {
                    HandlingPacket = false;
                }

                SrLogger.LogMessage($"Sanctuary room {roomId}: pending theme {refId} applied as the room loads");
            }
        }

        if (PendingPreplaced.Remove(roomId!, out var preplaced))
            WritePreplaced(roomId!, model, preplaced.Select(p => (p.Key, p.Value)));
    }

    #endregion

    #region Join snapshot

    internal static InitialSanctuaryPacket CreateInitialPacket()
    {
        var packet = new InitialSanctuaryPacket();
        var seen = new HashSet<string>();

        var rooms = GameState?.AllSanctuaryRooms();
        if (rooms != null)
        {
            foreach (var pair in rooms)
            {
                var id = pair.Key;
                var model = pair.Value;
                if (string.IsNullOrEmpty(id) || model == null)
                    continue;

                var room = new InitialSanctuaryPacket.Room { RoomId = id };

                try
                {
                    room.ThemeRefId = model.GetTheme()?.ReferenceId ?? string.Empty;
                }
                catch
                {
                    // no theme picked yet
                }

                try
                {
                    var preplaced = model._preplacedGadgets;
                    if (preplaced != null)
                    {
                        foreach (var entry in preplaced)
                        {
                            var gadgetId = entry.Key.Value;
                            var actorId = entry.Value.Value;
                            if (!string.IsNullOrEmpty(gadgetId) && actorId != 0)
                                room.PreplacedGadgets.Add((gadgetId, actorId));
                        }
                    }
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Could not read pre-placed gadgets of sanctuary room {id}: {ex.Message}");
                }

                if (PendingPreplaced.TryGetValue(id, out var pendingPreplaced))
                {
                    foreach (var (gadgetId, actorId) in pendingPreplaced)
                    {
                        if (room.PreplacedGadgets.All(p => p.GadgetId != gadgetId))
                            room.PreplacedGadgets.Add((gadgetId, actorId));
                    }
                }

                if (string.IsNullOrEmpty(room.ThemeRefId) && PendingThemes.TryGetValue(id, out var pendingTheme))
                    room.ThemeRefId = pendingTheme;

                seen.Add(id);
                packet.Rooms.Add(room);
            }
        }

        // Rooms the host only heard about from other players.
        foreach (var id in PendingThemes.Keys.Concat(PendingPreplaced.Keys).Distinct())
        {
            if (!seen.Add(id))
                continue;

            var room = new InitialSanctuaryPacket.Room
            {
                RoomId = id,
                ThemeRefId = PendingThemes.GetValueOrDefault(id) ?? string.Empty
            };

            if (PendingPreplaced.TryGetValue(id, out var preplaced))
                room.PreplacedGadgets.AddRange(preplaced.Select(p => (p.Key, p.Value)));

            packet.Rooms.Add(room);
        }

        return packet;
    }

    internal static void ApplyInitialPacket(InitialSanctuaryPacket packet)
    {
        try
        {
            var hostIds = new HashSet<string>(packet.Rooms.Select(r => r.RoomId));
            HostWorldState.DropEntriesHostLacks(GameState?.sanctuaryRooms, hostIds, IsRoomLive, "sanctuary rooms");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not compare sanctuary rooms with the host's world: {ex.Message}");
        }

        var themes = 0;
        foreach (var room in packet.Rooms)
        {
            try
            {
                if (!string.IsNullOrEmpty(room.ThemeRefId))
                {
                    ApplyTheme(room.RoomId, room.ThemeRefId);
                    themes++;
                }
            }
            catch (Exception ex)
            {
                HandlingPacket = false;
                SrLogger.LogWarning($"Could not apply the theme of sanctuary room {room.RoomId}: {ex.Message}");
            }

            try
            {
                ApplyPreplaced(room.RoomId, room.PreplacedGadgets);
            }
            catch (Exception ex)
            {
                HandlingPacket = false;
                SrLogger.LogWarning($"Could not apply the pre-placed gadgets of sanctuary room {room.RoomId}: {ex.Message}");
            }
        }

        SrLogger.LogMessage($"Applied the host's sanctuary: {packet.Rooms.Count} rooms, {themes} themes.");
    }

    #endregion
}
