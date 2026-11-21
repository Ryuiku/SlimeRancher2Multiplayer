using System.Security.Cryptography;
using System.Text;

namespace SR2MP.Shared.Utils;

internal static class PlayerIdGenerator
{
    private static string? cachedPlayerId;

    public static string GeneratePersistentPlayerId()
    {
        if (cachedPlayerId != null)
            return cachedPlayerId;

        try
        {
            // The game folder is part of the identity: a second copy of the game on the same PC
            // (for testing alone) is then a second player. With the same id both copies would
            // believe they simulate every slime.
            var hashBytes = DevMode
                ? RandomNumberGenerator.GetBytes(16)
                : SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName + GameFolderKey()));

            var hash = BitConverter.ToString(hashBytes)
                .Replace("-", string.Empty)[..9]
                .ToUpperInvariant();

            cachedPlayerId = $"PLAYER_{hash}";

            SrLogger.LogMessage($"Generated persistent player ID: {cachedPlayerId}");
            return cachedPlayerId;
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Failed to generate persistent player ID: {ex}");
            return null!;
        }
    }

    private static string GameFolderKey()
    {
        try
        {
            var folder = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
            if (string.IsNullOrEmpty(folder))
                return string.Empty;

            return Path.GetFullPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static ushort GetPlayerIDNumber(string id)
    {
        ushort number = 12345;
        foreach (var c in id[7..])
            number = (ushort)((number << 5) + number + c);
        return number;
    }

    // public static bool IsValidPlayerId(string playerId)
    // {
    //     if (string.IsNullOrWhiteSpace(playerId))
    //         return false;

    //     if (!playerId.StartsWith("PLAYER_"))
    //         return false;

    //     return playerId.Length == 16;
    // }
}