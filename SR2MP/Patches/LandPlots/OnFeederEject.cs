using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player;
using SR2MP.Packets.LandPlots;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.LandPlots;

[HarmonyPatch(typeof(SlimeFeeder), nameof(SlimeFeeder.EjectFood))]
internal static class OnFeederEject
{
    public static void Postfix(SlimeFeeder __instance, AmmoSlotManager storageAmmo)
    {
        if (HandlingPacket) return;

        // A joining player is still in their own save.
        if (!NetworkActorManager.CanSendActorChanges) return;

        var location = __instance.gameObject.GetComponentInParent<LandPlotLocation>();
        if (!location || string.IsNullOrEmpty(location._id)) return;

        var model = __instance._model;
        var packet = new AutoFeederDispensePacket()
        {
            ID = location._id,
            NextTime = __instance._nextEject,
            NextFeedingTime = model?.nextFeedingTime ?? 0d,
            RemainingFeedOperations = model?.remainingFeedOperations ?? 0,
        };

        Main.SendToAllOrServer(packet);
    }
}