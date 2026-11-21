using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime.Slumber;
using SR2MP.Components.Actor;
using SR2MP.Packets.Slime.Sloomber;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Slime.Sloomber;

// The owner tells the others when a sloomber starts winding up a yawn (the yawn face and body
// animation before the cloud), and when it stops before yawning. Before, the others only saw the
// yawn itself appear out of nowhere.
[HarmonyPatch(typeof(InitiateYawn))]
internal static class InitiateYawnWindUp
{
    private const byte BeginSent = 1;
    private const byte Yawned = 2;

    // How far each owned sloomber's current yawn has been sent. No entry: idle.
    private static readonly Dictionary<long, byte> Cycles = new();

    private static bool loggedError;

    static InitiateYawnWindUp()
    {
        Main.Server.OnServerStarted +=      Cycles.Clear;
        Main.Client.OnConnected     += _ => Cycles.Clear();
        Main.Client.OnDisconnected  +=      Cycles.Clear;
    }

    /// <summary>
    /// Called by the DoYawn sender: the yawn of this cycle has been sent, so no wind-up is sent
    /// again until the sloomber is idle.
    /// </summary>
    internal static void MarkYawned(long actorId) => Cycles[actorId] = Yawned;

    private static void LogOnce(string where, Exception exception)
    {
        if (loggedError)
            return;

        loggedError = true;
        SrLogger.LogDebug($"Sloomber wind-up sync ({where}) failed: {exception.Message}");
    }

    private static bool TryGetOwnedActorId(InitiateYawn yawn, out long actorId)
    {
        actorId = 0;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return false;

        if (HandlingPacket || !NetworkActorManager.CanSendActorChanges)
            return false;

        var networkActor = yawn.GetComponent<NetworkActor>();
        if (!networkActor || !networkActor.LocallyOwned)
            return false;

        actorId = networkActor.ActorId.Value;
        return actorId != 0;
    }

    private static void Check(InitiateYawn yawn, bool started = false)
    {
        var state = yawn._state;
        Note(yawn, state == InitiateYawn.State.IDLE,
            state is InitiateYawn.State.BEGIN_YAWNFACE or InitiateYawn.State.BEGIN_YAWN, started);
    }

    /// <summary>
    /// Owner only (checked here): this sloomber's yawn, run by InitiateYawn or by SlimeNap while it
    /// naps, is idle or winding up. started: a new yawn just began, so an entry left from an earlier
    /// cycle (a nap yawn marks it yawned and never reaches InitiateYawn's idle) cannot hide its wind-up.
    /// </summary>
    internal static void Note(InitiateYawn yawn, bool idle, bool windingUp, bool started = false)
    {
        if (!TryGetOwnedActorId(yawn, out var actorId))
            return;

        if (idle || started)
            Cycles.Remove(actorId);

        if (idle || !windingUp || Cycles.ContainsKey(actorId))
            return;

        Cycles[actorId] = BeginSent;
        Main.SendToAllOrServer(new SloomberYawnPacket { ActorId = new Il2CppMonomiPark.SlimeRancher.DataModel.ActorId(actorId), Active = true });
    }

    /// <summary>
    /// The yawn stopped before its cloud (the behaviour was deselected): cancel a wind-up that was
    /// sent, and forget the cycle.
    /// </summary>
    internal static void NoteStopped(InitiateYawn yawn)
    {
        if (Cycles.Count == 0)
            return;

        var networkActor = yawn.GetComponent<NetworkActor>();
        if (!networkActor)
            return;

        // Forgotten even when the slime is no longer ours, so a later wind-up is sent again.
        var actorId = networkActor.ActorId.Value;
        if (!Cycles.Remove(actorId, out var cycle) || cycle != BeginSent)
            return;

        if (TryGetOwnedActorId(yawn, out _))
        {
            Main.SendToAllOrServer(new SloomberYawnPacket
            {
                ActorId = new Il2CppMonomiPark.SlimeRancher.DataModel.ActorId(actorId),
                Active = false,
                Cancel = true
            });
        }
    }

    [HarmonyPatch(nameof(InitiateYawn.Selected)), HarmonyPostfix]
    public static void SelectedPostfix(InitiateYawn __instance, bool __runOriginal)
    {
        try
        {
            // A Selected always starts a new yawn.
            if (__runOriginal)
                Check(__instance, started: true);
        }
        catch (Exception exception)
        {
            LogOnce("Selected", exception);
        }
    }

    [HarmonyPatch(nameof(InitiateYawn.Action)), HarmonyPostfix]
    public static void ActionPostfix(InitiateYawn __instance, bool __runOriginal)
    {
        try
        {
            if (__runOriginal)
                Check(__instance);
        }
        catch (Exception exception)
        {
            LogOnce("Action", exception);
        }
    }

    // The sloomber stopped (picked up, scared, another behaviour won) during its wind-up.
    [HarmonyPatch(nameof(InitiateYawn.Deselected)), HarmonyPostfix]
    public static void DeselectedPostfix(InitiateYawn __instance, bool __runOriginal)
    {
        try
        {
            if (__runOriginal)
                NoteStopped(__instance);
        }
        catch (Exception exception)
        {
            LogOnce("Deselected", exception);
        }
    }
}
