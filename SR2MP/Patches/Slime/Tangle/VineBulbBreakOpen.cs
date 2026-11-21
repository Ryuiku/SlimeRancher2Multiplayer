using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Components.Actor;

namespace SR2MP.Patches.Slime.Tangle;

// A tangle bud spawns its contents when it breaks or expires. Only the bud's owner does that, so
// the contents appear once (the same rule as the Yolky giant egg).
[HarmonyPatch(typeof(VineBulbBreakOnImpact), nameof(VineBulbBreakOnImpact.BreakOpen))]
internal static class VineBulbBreakOpen
{
    public static bool Prefix(VineBulbBreakOnImpact __instance)
    {
        try
        {
            if (!Main.Server.IsRunning && !Main.Client.IsConnected) return true;
            if (HandlingPacket) return true;

            var networkActor = __instance.GetComponent<NetworkActor>();
            return !networkActor || networkActor.LocallyOwned;
        }
        catch (Exception exception)
        {
            SrLogger.LogDebug($"VineBulbBreakOnImpact.BreakOpen gate failed: {exception.Message}");
            return true;
        }
    }
}
