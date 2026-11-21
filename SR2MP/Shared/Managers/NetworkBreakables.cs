using SR2MP.Components.Actor;
using SR2MP.Packets.World;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Breakable crates are actors, so every player has a copy and a hit can break any of them. Only
/// the owner's copy may break: the others ask the owner, so the rewards and newbucks come once.
/// </summary>
internal static class NetworkBreakables
{
    // True while the owner breaks a crate because another player asked; the break patch lets it through.
    internal static bool ApplyingRequest;

    // How often one crate may be asked about. A crate lying against a moving object collides every frame.
    private const float RequestInterval = 1f;

    // A request the owner has not acted on after this long (their copy is not loaded, say) lets
    // this player claim the crate and break it here, so no crate stays unbreakable.
    private const float RequestTimeout = 2f;

    // Actor id -> time of the first request still unanswered, and of the last one sent.
    private static readonly Dictionary<long, float> FirstRequest = new();
    private static readonly Dictionary<long, float> LastRequest = new();

    static NetworkBreakables()
    {
        Main.Server.OnServerStarted +=      Clear;
        Main.Client.OnConnected     += _ => Clear();
        Main.Client.OnDisconnected  +=      Clear;
    }

    private static void Clear()
    {
        FirstRequest.Clear();
        LastRequest.Clear();
        ApplyingRequest = false;
    }

    /// <summary>
    /// Called for a crate this game does not own that was hit here. Returns true when it may break
    /// here after all (its owner is gone, or did not answer), after claiming it.
    /// </summary>
    internal static bool RequestBreak(NetworkActor networkActor)
    {
        var id = networkActor.ActorId.Value;
        if (id == 0)
            return true;

        var now = UnityEngine.Time.time;

        networkActor.EnsureOwnerInfo();
        var owner = networkActor.CurrentOwnerId;
        var ownerGone = string.IsNullOrEmpty(owner)
                        || (owner != LocalID && !PlayerManager.CheckPlayerExists(owner));

        var unanswered = FirstRequest.TryGetValue(id, out var first) && now - first > RequestTimeout;

        if (ownerGone || unanswered)
        {
            FirstRequest.Remove(id);
            LastRequest.Remove(id);

            if (!Patches.Actor.OnActorVacced.Claim(networkActor))
                return false;

            SrLogger.LogDebug(ownerGone
                ? $"Crate {id}: its owner left, breaking it here"
                : $"Crate {id}: its owner did not break it, breaking it here");
            return true;
        }

        if (LastRequest.TryGetValue(id, out var last) && now - last < RequestInterval)
            return false;

        if (!FirstRequest.ContainsKey(id))
            FirstRequest[id] = now;
        LastRequest[id] = now;

        Main.SendToAllOrServer(new BreakableBreakPacket { ActorId = id });
        SrLogger.LogDebug($"Crate {id} hit here; asked its owner {owner} to break it");
        return false;
    }

    /// <summary>
    /// The crate is gone (broken by its owner, or removed): forget any request for it.
    /// </summary>
    internal static void Forget(long actorId)
    {
        FirstRequest.Remove(actorId);
        LastRequest.Remove(actorId);
    }
}
