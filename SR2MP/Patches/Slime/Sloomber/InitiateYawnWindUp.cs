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

    private static void Check(InitiateYawn yawn)
    {
        if (!TryGetOwnedActorId(yawn, out var actorId))
            return;

        var state = yawn._state;

        if (state == InitiateYawn.State.IDLE)
        {
            Cycles.Remove(actorId);
            return;
        }

        if (state is not (InitiateYawn.State.BEGIN_YAWNFACE or InitiateYawn.State.BEGIN_YAWN))
            return;

        if (Cycles.ContainsKey(actorId))
            return;

        Cycles[actorId] = BeginSent;
        Main.SendToAllOrServer(new SloomberYawnPacket { ActorId = new Il2CppMonomiPark.SlimeRancher.DataModel.ActorId(actorId), Active = true });
    }

    [HarmonyPatch(nameof(InitiateYawn.Selected)), HarmonyPostfix]
    public static void SelectedPostfix(InitiateYawn __instance, bool __runOriginal)
    {
        try
        {
            if (__runOriginal)
                Check(__instance);
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
            if (!__runOriginal || Cycles.Count == 0)
                return;

            var networkActor = __instance.GetComponent<NetworkActor>();
            if (!networkActor)
                return;

            // Forgotten even when the slime is no longer ours, so a later wind-up is sent again.
            var actorId = networkActor.ActorId.Value;
            if (!Cycles.Remove(actorId, out var cycle) || cycle != BeginSent)
                return;

            if (TryGetOwnedActorId(__instance, out _))
            {
                Main.SendToAllOrServer(new SloomberYawnPacket
                {
                    ActorId = new Il2CppMonomiPark.SlimeRancher.DataModel.ActorId(actorId),
                    Active = false,
                    Cancel = true
                });
            }
        }
        catch (Exception exception)
        {
            LogOnce("Deselected", exception);
        }
    }
}
