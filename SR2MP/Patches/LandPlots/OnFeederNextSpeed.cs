using HarmonyLib;
using SR2MP.Packets.LandPlots;

namespace SR2MP.Patches.LandPlots;

[HarmonyPatch(typeof(SlimeFeeder), nameof(SlimeFeeder.StepFeederSpeed))]
internal static class OnFeederNextSpeed
{
    public static void Postfix(SlimeFeeder __instance)
    {
        if (HandlingPacket) return;

        try
        {
            var location = __instance.GetComponentInParent<LandPlotLocation>(true);
            var model = __instance._model;
            if (!location || string.IsNullOrEmpty(location._id) || model == null)
                return;

            // The speed the press left in the model, not a step, so a repeated packet changes nothing.
            Main.SendToAllOrServer(new AutoFeederSpeedPacket
            {
                Speed = model.feederCycleSpeed,
                ID = location._id,
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not send a feeder speed: {ex.Message}");
        }
    }
}
