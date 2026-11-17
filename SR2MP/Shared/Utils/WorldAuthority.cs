using SR2MP.Shared.Managers;

namespace SR2MP.Shared.Utils;

/// <summary>
/// For world objects that run their own timers on every game (geysers, linked cannons) and have no
/// owner. Of the players near the object, the one with the lowest player id runs it and the others
/// only replay what it sends. Every game works this out from the same synced positions, so they
/// agree without a packet.
/// </summary>
internal static class WorldAuthority
{
    internal static bool IsLocalAuthority(Vector3 position, float radius)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return true;

        try
        {
            var localId = LocalID;
            var player = SceneContext.Instance ? SceneContext.Instance.Player : null;
            if (!player)
                return false;

            var radiusSquared = radius * radius;
            if ((player!.transform.position - position).sqrMagnitude > radiusSquared)
                return false;

            var localSceneGroup = NetworkSceneManager.GetPersistentID(
                SystemContext.Instance.SceneLoader._currentSceneGroup);

            foreach (var remote in PlayerManager.GetAllPlayers())
            {
                if (remote == null || string.IsNullOrEmpty(remote.PlayerId) || remote.PlayerId == localId)
                    continue;

                if (localSceneGroup < 0 || remote.SceneGroup != localSceneGroup)
                    continue;

                if ((remote.Position - position).sqrMagnitude > radiusSquared)
                    continue;

                if (string.CompareOrdinal(remote.PlayerId, localId) < 0)
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Running it here as well is today's behaviour; never running it would stop the object.
            SrLogger.LogDebug($"Could not work out who runs a world object: {ex.Message}");
            return true;
        }
    }
}
