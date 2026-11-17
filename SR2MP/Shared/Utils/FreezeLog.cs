using System.Text;

namespace SR2MP.Shared.Utils;

/// <summary>
/// The last things that froze, locked or released the local player, kept in memory. Each line
/// also goes to the debug log; the whole list is written out only when a freeze looks stuck or
/// F6 is pressed, so a normal session does not fill the log.
/// </summary>
internal static class FreezeLog
{
    private const int Capacity = 30;
    private static readonly Queue<string> Lines = new();
    private static readonly object Lock = new();

    /// <summary>
    /// Whether the local character controller was last told to freeze (SRCharacterController.SetFreeze(true)).
    /// The game keeps no readable flag for this, so it is followed here.
    /// </summary>
    internal static bool ControllerFrozen { get; private set; }

    /// <summary>realtimeSinceStartup when the controller was frozen.</summary>
    internal static float FrozenSince { get; private set; }

    /// <summary>Who froze it: whether a packet was being applied, and the mod's own frames on the call stack.</summary>
    internal static string FrozenBy { get; private set; } = string.Empty;

    /// <param name="important">Also written to the normal log. For the rare events that matter
    /// most when a player reports being stuck (freeze, lock, time skip), not for menu pauses.</param>
    internal static void Record(string line, bool important = false)
    {
        try
        {
            var worldTime = 0.0;
            try
            {
                if (SceneContext.Instance && SceneContext.Instance.TimeDirector)
                    worldTime = SceneContext.Instance.TimeDirector._worldModel.worldTime;
            }
            catch
            {
                // no world loaded
            }

            var stamped = $"[{UnityEngine.Time.realtimeSinceStartup:F2}s wt={worldTime:F0}] {line}";
            lock (Lock)
            {
                Lines.Enqueue(stamped);
                while (Lines.Count > Capacity)
                    Lines.Dequeue();
            }

            if (important)
                SrLogger.LogMessage($"Freeze log: {line}");
            else
                SrLogger.LogDebug($"Freeze log: {line}");
        }
        catch
        {
            // never get in the way of the game
        }
    }

    internal static void SetControllerFrozen(bool frozen, string by)
    {
        if (frozen && !ControllerFrozen)
            FrozenSince = UnityEngine.Time.realtimeSinceStartup;

        ControllerFrozen = frozen;
        FrozenBy = frozen ? by : string.Empty;
    }

    /// <summary>
    /// A session ended or a new world loaded: whatever was frozen belonged to the old player object.
    /// </summary>
    internal static void ResetControllerState()
    {
        ControllerFrozen = false;
        FrozenBy = string.Empty;
    }

    /// <summary>
    /// The mod's own frames on the current call stack, so a log line shows whether a packet
    /// handler or patch of this mod is behind a game call.
    /// </summary>
    internal static string ModCallers()
    {
        try
        {
            var frames = new System.Diagnostics.StackTrace(1, false).GetFrames();
            var names = new List<string>();
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                if (type?.FullName == null || !type.FullName.StartsWith("SR2MP.", StringComparison.Ordinal))
                    continue;
                // This class and the logging postfixes themselves (Patches/Player/OnPlayerFreeze.cs).
                if (type == typeof(FreezeLog) || type.Name == "OnPlayerFreezeHelpers"
                    || (type.Namespace == "SR2MP.Patches.Player" && method!.Name == "Postfix"))
                    continue;

                names.Add($"{type.Name}.{method!.Name}");
                if (names.Count >= 4)
                    break;
            }

            return names.Count == 0 ? "game" : string.Join(" < ", names);
        }
        catch
        {
            return "unknown";
        }
    }

    internal static string Dump()
    {
        var builder = new StringBuilder("Freeze log (oldest first):");
        lock (Lock)
        {
            if (Lines.Count == 0)
                builder.Append("\n    (empty)");

            foreach (var line in Lines)
                builder.Append("\n    ").Append(line);
        }

        return builder.ToString();
    }
}
