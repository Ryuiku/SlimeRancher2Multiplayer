using SR2MP.Shared.Managers;

namespace SR2MP.Shared.Utils;

/// <summary>
/// For world objects that run their own timers on every game (geysers, linked cannons) and have no
/// owner. Of the players near the object, the one with the lowest player id runs it and the others
/// only replay what it sends. When nobody is near, the nearest player in the same scene runs it, so
/// it still runs exactly once instead of on no game at all. Every game works this out from the same
/// synced positions, so they agree without a packet.
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
            var localDistance = (player!.transform.position - position).sqrMagnitude;
            var localInRange = localDistance <= radiusSquared;

            var localSceneGroup = NetworkSceneManager.GetPersistentID(
                SystemContext.Instance.SceneLoader._currentSceneGroup);

            // Nearest player of this scene so far, for when nobody is in range.
            var nearestId = localId;
            var nearestDistance = localDistance;

            foreach (var remote in PlayerManager.GetAllPlayers())
            {
                if (remote == null || string.IsNullOrEmpty(remote.PlayerId) || remote.PlayerId == localId)
                    continue;

                if (localSceneGroup < 0 || remote.SceneGroup != localSceneGroup)
                    continue;

                var distance = (remote.Position - position).sqrMagnitude;
                if (distance <= radiusSquared)
                {
                    // Someone near it runs it: them alone when we are not near, else the lower id.
                    if (!localInRange || string.CompareOrdinal(remote.PlayerId, localId) < 0)
                        return false;
                    continue;
                }

                if (distance < nearestDistance
                    || (distance == nearestDistance && string.CompareOrdinal(remote.PlayerId, nearestId) < 0))
                {
                    nearestDistance = distance;
                    nearestId = remote.PlayerId;
                }
            }

            if (localInRange)
                return true;

            // Nobody near it. The nearest player is the one most likely to have it loaded.
            return nearestId == localId;
        }
        catch (Exception ex)
        {
            // Running it here as well is today's behaviour; never running it would stop the object.
            SrLogger.LogDebug($"Could not work out who runs a world object: {ex.Message}");
            return true;
        }
    }
}
