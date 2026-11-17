using HarmonyLib;
using SR2MP.Packets.Gadget;
using SR2MP.Shared.Managers;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.Gadget;

[HarmonyPatch(typeof(LinkedCannonInput), nameof(LinkedCannonInput.Update))]
internal static class OnCannonFire
{
    public static void Postfix(LinkedCannonInput __instance)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;
        if (HandlingPacket) return;

        try
        {
            if (SystemContext.Instance.SceneLoader.IsSceneLoadInProgress) return;

            var gadget = __instance._gadget;
            if (!gadget) return;

            var gadgetId = gadget.GetActorId().Value;

            if (!NetworkGadgetManager.TryRecordCannonFireTime(gadgetId, __instance._nextFireTime))
                return;

            Main.SendToAllOrServer(new CannonFirePacket
            {
                GadgetId = gadgetId,
                NextFireTime = __instance._nextFireTime
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"OnCannonFire: failed to broadcast cannon timer: {ex.Message}");
        }
    }
}


// With a shared world clock every game reaches the cannon's fire time in the same frame and fires
// its own shot (its own item, its own ammo taken). Of the players near the cannon, one fires it;
// the others get the shot as an actor spawn, the ammo through the silo sync and the timer from
// the CannonFire packet.
[HarmonyPatch(typeof(LinkedCannonInput), nameof(LinkedCannonInput.ShootLinked))]
internal static class OnCannonShoot
{
    private const float Radius = 100f;

    public static bool Prefix(LinkedCannonInput __instance)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected) return true;
            if (HandlingPacket) return true;
            if (SystemContext.Instance.SceneLoader.IsSceneLoadInProgress) return true;

            return WorldAuthority.IsLocalAuthority(__instance.transform.position, Radius);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not check linked cannon shot: {ex.Message}");
            return true;
        }
    }
}
