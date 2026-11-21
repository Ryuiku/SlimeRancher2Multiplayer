using System.Buffers;
using System.Collections.Concurrent;

namespace SR2MP.Shared.Utils;

internal static class PacketDeduplication
{
    // Key format: "PacketType_UniqueId"
    private static readonly ConcurrentDictionary<PacketKey, DateTime> ProcessedPackets = new();

    private static readonly TimeSpan PacketMemoryDuration = TimeSpan.FromSeconds(30);

    // Cleanup runs on elapsed time. It used to run every 100 packets, so on a quiet connection
    // records stayed far longer than the 30 seconds they are meant to.
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(5);
    private static long lastCleanupTicks;

    public static bool IsDuplicate(PacketKey key)
    {
        var now = DateTime.UtcNow;
        var last = Interlocked.Read(ref lastCleanupTicks);

        if (now.Ticks - last >= CleanupInterval.Ticks
            && Interlocked.CompareExchange(ref lastCleanupTicks, now.Ticks, last) == last)
        {
            Cleanup();
        }

        return !ProcessedPackets.TryAdd(key, now);
    }

    /// <summary>
    /// Returns true if a packet with this key was already fully received and processed recently.
    /// Does not record anything.
    /// </summary>
    public static bool WasProcessed(PacketKey key) => ProcessedPackets.ContainsKey(key);

    // public static void MarkProcessed(PacketKey key)
    //     => ProcessedPackets[key] = DateTime.UtcNow;

    public static void Clear()
    {
        ProcessedPackets.Clear();
        SrLogger.LogPacketSize("Packet deduplication cache cleared");
        Cleanup();
    }

    private static void Cleanup()
    {
        var count = ProcessedPackets.Count;
        if (count == 0) return;

        var keysToRemove = ArrayPool<PacketKey>.Shared.Rent(count);
        var removeCount = 0;
        var now = DateTime.UtcNow;

        foreach (var kvp in ProcessedPackets)
        {
            if (now - kvp.Value > PacketMemoryDuration)
            {
                keysToRemove[removeCount++] = kvp.Key;
            }
        }

        for (var i = 0; i < removeCount; i++)
        {
            ProcessedPackets.TryRemove(keysToRemove[i], out _);
        }

        if (removeCount > 0)
        {
            SrLogger.LogPacketSize($"Cleaned up {removeCount} old packet records");
        }

        ArrayPool<PacketKey>.Shared.Return(keysToRemove);
    }

    // public static int GetTrackedPacketCount() => ProcessedPackets.Count;
}