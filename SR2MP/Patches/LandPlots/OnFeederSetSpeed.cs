using HarmonyLib;
using SR2MP.Packets.LandPlots;

namespace SR2MP.Patches.LandPlots;

[HarmonyPatch(typeof(SlimeFeeder), nameof(SlimeFeeder.SetFeederSpeed))]
internal static class OnFeederSetSpeed
{
    public static void Postfix(SlimeFeeder __instance, SlimeFeeder.FeedSpeed speed)
    {
        if (HandlingPacket) return;

        try
        {
            var location = __instance.GetComponentInParent<LandPlotLocation>(true);
            if (!location || string.IsNullOrEmpty(location._id) || __instance._model == null)
                return;

            Main.SendToAllOrServer(new AutoFeederSpeedPacket
            {
                Speed = speed,
                ID = location._id,
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not send a feeder speed: {ex.Message}");
        }
    }
}
