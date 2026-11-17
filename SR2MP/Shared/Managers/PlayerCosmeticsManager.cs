using System.Net;
using Il2CppMonomiPark.SlimeRancher.Ranch;
using SR2MP.Packets.Player;
using SR2MP.Shared.Utils;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Each player's own look (for now the vac palette). It comes from that player's own save and is
/// only shown on their avatar: nothing here writes another player's choice into the local game,
/// so everyone keeps their own palette in their own save, and anyone can rehost.
/// </summary>
internal static class PlayerCosmeticsManager
{
    private static readonly object Sync = new();

    // Player id -> vac palette reference id ("" = the game's default).
    private static readonly Dictionary<string, string> VacPaletteByPlayer = new();

    // What this player last told the others. Null means "not told yet", so the next poll sends.
    private static string? lastSentVacPalette;
    private static float nextPollTime;
    private const float PollInterval = 1f;

    private static bool warnedLookup;

    internal static void Set(string playerId, string? vacPalette)
    {
        lock (Sync)
            VacPaletteByPlayer[playerId] = vacPalette ?? string.Empty;
    }

    internal static bool TryGet(string playerId, out string vacPalette)
    {
        lock (Sync)
        {
            if (VacPaletteByPlayer.TryGetValue(playerId, out var found))
            {
                vacPalette = found;
                return true;
            }
        }

        vacPalette = string.Empty;
        return false;
    }

    internal static void Remove(string playerId)
    {
        lock (Sync)
            VacPaletteByPlayer.Remove(playerId);
    }

    internal static void Clear()
    {
        lock (Sync)
            VacPaletteByPlayer.Clear();

        lastSentVacPalette = null;
    }

    #region Lookups

    private static PaletteDirector? Director =>
        SceneContext.Instance ? SceneContext.Instance.PaletteDirector : null;

    internal static bool DirectorReady => Director;

    internal static VacPaletteTarget? FindVacTarget()
    {
        var director = Director;
        if (!director)
            return null;

        try
        {
            foreach (var target in director!._paletteTargets.items)
            {
                var vacTarget = target ? target.TryCast<VacPaletteTarget>() : null;
                if (vacTarget != null)
                    return vacTarget;
            }
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not find the vac palette target: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// The vac palette with this id; an empty or unknown id gives the game's default vac palette.
    /// </summary>
    internal static VacPalette? FindVacPalette(string? refId)
    {
        try
        {
            if (!string.IsNullOrEmpty(refId))
            {
                var palette = UnlockLookup.FindPalette(refId!);
                var vacPalette = palette ? palette!.TryCast<VacPalette>() : null;
                if (vacPalette != null)
                    return vacPalette;

                // Also look in the director directly, in case the shared lookup was built too early.
                var director = Director;
                if (director)
                {
                    foreach (var candidate in director!._palettes.items)
                    {
                        if (candidate && candidate.ReferenceId == refId)
                        {
                            vacPalette = candidate.TryCast<VacPalette>();
                            if (vacPalette != null)
                                return vacPalette;
                        }
                    }
                }
            }

            var target = FindVacTarget();
            return target != null ? target.DefaultVacPalette : null;
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not look up vac palette {refId}: {ex.Message}");
            return null;
        }
    }

    internal static List<VacPalette> GetAllVacPalettes()
    {
        var list = new List<VacPalette>();
        var director = Director;
        if (!director)
            return list;

        try
        {
            foreach (var candidate in director!._palettes.items)
            {
                var vacPalette = candidate ? candidate.TryCast<VacPalette>() : null;
                if (vacPalette != null)
                    list.Add(vacPalette);
            }

            var fallback = FindVacTarget()?.DefaultVacPalette;
            if (fallback && !list.Exists(p => p.Pointer == fallback!.Pointer))
                list.Add(fallback!);
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not list the vac palettes: {ex.Message}");
        }

        return list;
    }

    /// <summary>The vac palette this player picked in their own save ("" if unknown).</summary>
    internal static string GetLocalVacPaletteId()
    {
        try
        {
            var director = Director;
            var target = FindVacTarget();
            if (!director || target == null)
                return string.Empty;

            var palette = director!.GetPaletteForTarget(target);
            return palette ? palette.ReferenceId ?? string.Empty : string.Empty;
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not read the local vac palette: {ex.Message}");
            return string.Empty;
        }
    }

    #endregion

    #region Sending

    /// <summary>
    /// Called when the local player's network component starts (on connecting, and after a scene
    /// reload): the next poll sends the palette again even if it did not change.
    /// </summary>
    internal static void ResetLocal()
    {
        lastSentVacPalette = null;
        nextPollTime = 0f;
    }

    // Polled instead of patching the palette setter: which method the shop and the ranch house
    // selector call is not known for sure, and a change only shows up to a second late.
    internal static void PollLocal()
    {
        var now = UnityEngine.Time.unscaledTime;
        if (now < nextPollTime)
            return;
        nextPollTime = now + PollInterval;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        if (!DirectorReady)
            return;

        var current = GetLocalVacPaletteId();
        if (current == lastSentVacPalette)
            return;

        var playerId = LocalID;
        if (string.IsNullOrEmpty(playerId))
            return;

        lastSentVacPalette = current;

        try
        {
            Main.SendToAllOrServer(new PlayerCosmeticsPacket
            {
                PlayerId = playerId,
                VacPalette = current
            });
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not send the vac palette: {ex.Message}");
        }
    }

    /// <summary>
    /// Join and resync snapshot: the host's own palette (read live) and every other player's,
    /// except the one of the player it is sent to.
    /// </summary>
    internal static void SendSnapshotTo(string playerId, IPEndPoint endPoint)
    {
        try
        {
            Main.Server.SendToClient(new PlayerCosmeticsPacket
            {
                PlayerId = Main.Server.PlayerId,
                VacPalette = GetLocalVacPaletteId()
            }, endPoint);

            List<KeyValuePair<string, string>> entries;
            lock (Sync)
                entries = VacPaletteByPlayer.ToList();

            foreach (var (id, vacPalette) in entries)
            {
                if (id == playerId || id == Main.Server.PlayerId)
                    continue;

                Main.Server.SendToClient(new PlayerCosmeticsPacket
                {
                    PlayerId = id,
                    VacPalette = vacPalette
                }, endPoint);
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not send the players' vac palettes: {ex.Message}");
        }
    }

    #endregion

    private static void WarnOnce(string message)
    {
        if (warnedLookup)
            return;

        warnedLookup = true;
        SrLogger.LogWarning(message);
    }
}
