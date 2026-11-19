using HarmonyLib;
using SR2MP.Packets.LandPlots;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.LandPlots;

[HarmonyPatch(typeof(LandPlot), nameof(LandPlot.DestroyAttached))]
internal static class OnDestroyCrop
{
    public static void Postfix(LandPlot __instance)
    {
        if (HandlingPacket) return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        if (!__instance)
            return;

        var location = __instance.GetComponentInParent<LandPlotLocation>();
        if (!location || string.IsNullOrEmpty(location!._id))
            return;

        var packet = new GardenPlantPacket
        {
            ID = location._id,
            ActorType = NetworkGardenPlanting.ClearCrop
        };

        Main.SendToAllOrServer(packet);
    }
}
