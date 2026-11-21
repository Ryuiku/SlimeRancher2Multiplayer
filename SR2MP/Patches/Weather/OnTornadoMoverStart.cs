using HarmonyLib;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Weather;

[HarmonyPatch(typeof(SlimenadoMoveSpiral), nameof(SlimenadoMoveSpiral.Start))]
internal static class OnTornadoMoverStart
{
    // Start always sets _rot to 90, so a copy made for a tornado that has already been turning
    // for a while gets the host's current spin back here, before its first FixedUpdate.
    public static void Postfix(SlimenadoMoveSpiral __instance)
    {
        try
        {
            if (NetworkTornadoManager.PendingSpins.Count != 0 &&
                NetworkTornadoManager.PendingSpins.Remove(__instance.GetInstanceID(), out var spin))
                __instance._rot = spin;
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"OnTornadoMoverStart: {ex.Message}");
        }
    }
}
