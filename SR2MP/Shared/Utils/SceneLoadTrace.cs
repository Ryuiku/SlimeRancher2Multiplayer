using System.Text;

namespace SR2MP.Shared.Utils;

/// <summary>
/// The last things that happened around zone loads, kept in memory and written to the log only
/// when a load fails or the game returns to the main menu. Meant to show what led up to a kick.
/// </summary>
internal static class SceneLoadTrace
{
    private const int Capacity = 80;
    private static readonly Queue<string> Lines = new();
    private static readonly object Lock = new();

    // Packet handlers that ran while a zone was loading, counted by packet type.
    private static readonly Dictionary<string, int> HandledDuringLoad = new();

    internal static void Record(string line)
    {
        try
        {
            var stamped = $"[{UnityEngine.Time.realtimeSinceStartup:F2}s f{UnityEngine.Time.frameCount}] {line}";
            lock (Lock)
            {
                Lines.Enqueue(stamped);
                while (Lines.Count > Capacity)
                    Lines.Dequeue();
            }
        }
        catch
        {
            // never get in the way of the game
        }
    }

    internal static void CountHandledDuringLoad(string packetType)
    {
        lock (Lock)
            HandledDuringLoad[packetType] = HandledDuringLoad.GetValueOrDefault(packetType) + 1;
    }

    /// <summary>
    /// Writes the packets handled during the load that just ended as one line.
    /// </summary>
    internal static void FlushHandledDuringLoad()
    {
        string? line = null;
        lock (Lock)
        {
            if (HandledDuringLoad.Count > 0)
            {
                line = "Packets handled during the load: " +
                       string.Join(", ", HandledDuringLoad.OrderByDescending(p => p.Value).Select(p => $"{p.Key} x{p.Value}"));
                HandledDuringLoad.Clear();
            }
        }

        if (line != null)
            Record(line);
    }

    internal static void Dump(string reason)
    {
        try
        {
            FlushHandledDuringLoad();

            var builder = new StringBuilder();
            builder.AppendLine($"Zone load trace ({reason}), oldest first:");
            lock (Lock)
            {
                foreach (var line in Lines)
                    builder.AppendLine("  " + line);
            }

            SrLogger.LogError(builder.ToString());
        }
        catch
        {
            // ignored
        }
    }
}
