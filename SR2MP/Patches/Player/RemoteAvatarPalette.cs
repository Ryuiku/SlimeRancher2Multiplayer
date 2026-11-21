using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Ranch;
using SR2MP.Components.Player;

namespace SR2MP.Patches.Player;

// The game colours everything that registers with its palette director in the local player's
// chosen palette. Another player's avatar must show their own choice instead, so its palette
// components never register here; the avatar applies the palette it got from that player.
[HarmonyPatch(typeof(ApplyPalette), nameof(ApplyPalette.Start))]
internal static class RemoteAvatarPaletteStart
{
    public static bool Prefix(ApplyPalette __instance) => !RemoteAvatarPalette.IsOnRemoteAvatar(__instance);
}

[HarmonyPatch(typeof(ApplyPalette), nameof(ApplyPalette.OnDestroy))]
internal static class RemoteAvatarPaletteOnDestroy
{
    public static bool Prefix(ApplyPalette __instance) => !RemoteAvatarPalette.IsOnRemoteAvatar(__instance);
}

internal static class RemoteAvatarPalette
{
    internal static bool IsOnRemoteAvatar(ApplyPalette applyPalette)
    {
        try
        {
            if (!applyPalette)
                return false;

            // Walked up by hand, so it also works while the avatar is still inactive.
            var current = applyPalette.transform;
            while (current)
            {
                var networkPlayer = current.GetComponent<NetworkPlayer>();
                if (networkPlayer)
                    return !networkPlayer.IsLocal;

                current = current.parent;
            }
        }
        catch
        {
            // Never break the game's own palettes over this.
        }

        return false;
    }
}
