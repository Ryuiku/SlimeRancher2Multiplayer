using HarmonyLib;
using SR2MP.Components.Actor;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.World;

// Crates break on whichever copy something hits hard enough, and each copy that breaks gives out
// its own rewards and newbucks. Only the owner's copy breaks; another player who hits it asks the
// owner instead. Yolky eggs and Tangle buds use their own BreakOpen(GameObject) and are not affected.
[HarmonyPatch(typeof(BreakOnImpactBase), nameof(BreakOnImpactBase.BreakOpen))]
internal static class OnBreakableBreakOpen
{
    public static bool Prefix(BreakOnImpactBase __instance)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected) return true;
            if (HandlingPacket || NetworkBreakables.ApplyingRequest) return true;
            if (__instance._breaking) return true;

            var networkActor = __instance.GetComponent<NetworkActor>();
            if (!networkActor)
                return true;

            if (networkActor.LocallyOwned)
            {
                NetworkBreakables.Forget(networkActor.ActorId.Value);
                return true;
            }

            return NetworkBreakables.RequestBreak(networkActor);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not check a crate before it broke: {ex.Message}");
            return true;
        }
    }
}
