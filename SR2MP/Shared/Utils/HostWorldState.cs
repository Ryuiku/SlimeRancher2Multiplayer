namespace SR2MP.Shared.Utils;

internal static class HostWorldState
{
    /// <summary>
    /// A joining player still has the doors, switches and puzzle state of their own save. The
    /// host's world replaces the entries the host knows about. Entries the host has no record of
    /// (areas the host's world never loaded) used to stay as they were in the joiner's save, and
    /// were announced to everyone when the joiner got there, changing the host's world.
    /// This drops them, so the game creates them fresh, as it does on the host.
    /// Entries that are loaded right now are left alone.
    /// </summary>
    internal static void DropEntriesHostLacks<T>(
        CppCollections.Dictionary<string, T>? local,
        HashSet<string> hostIds,
        Func<T, bool> isLoaded,
        string what) where T : Il2CppSystem.Object
    {
        if (local == null)
            return;

        try
        {
            var remove = new List<string>();

            foreach (var pair in local)
            {
                var key = pair.Key;
                if (key == null || hostIds.Contains(key))
                    continue;

                var value = pair.Value;
                if (value != null && isLoaded(value))
                    continue;

                remove.Add(key);
            }

            foreach (var key in remove)
                local.Remove(key);

            if (remove.Count > 0)
                SrLogger.LogMessage($"Left out {remove.Count} {what} from this save that the host's world has no record of.");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not compare {what} with the host's world: {ex.Message}");
        }
    }
}
