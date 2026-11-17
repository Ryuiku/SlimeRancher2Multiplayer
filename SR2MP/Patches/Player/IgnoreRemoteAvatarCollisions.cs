using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using SR2MP.Components.Player;

namespace SR2MP.Patches.Player;

// Other players never block your own movement: you walk through their avatars. An avatar can be
// a moment behind, or left standing where a player was before a teleport or a loading screen,
// and bumping into that (or being stopped in a doorway by it) only gets in the way.
// This is the character controller's own per-hit filter, asked for every sweep and overlap.
[HarmonyPatch(typeof(SRCharacterController), nameof(SRCharacterController.IsColliderValidForCollisions))]
internal static class IgnoreRemoteAvatarCollisions
{
    public static void Postfix(Collider coll, ref bool __result)
    {
        if (!__result || coll is null || NetworkPlayer.RemoteAvatarColliders.Count == 0)
            return;

        if (NetworkPlayer.RemoteAvatarColliders.Contains(coll.Pointer))
            __result = false;
    }
}
