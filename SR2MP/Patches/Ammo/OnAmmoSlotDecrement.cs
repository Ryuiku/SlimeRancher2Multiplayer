using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player;
using SR2MP.Packets.Ammo;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Ammo;

[HarmonyPatch(typeof(AmmoSlot), nameof(AmmoSlot.DecrementAmmo))]
internal static class OnAmmoSlotDecrement
{
    public static void Postfix(AmmoSlot __instance)
    {
        if ((!Main.Client.IsConnected && !Main.Server.IsRunning) || HandlingPacket) return;

        try
        {
            if (__instance._count <= 0) __instance._id = null;

            var id = __instance.GetPlotID();
            if (id == null) return;

            var index = __instance.GetSlotIndex();
            if (index == null) return;

            NetworkSprinkleCanisterManager.MarkDirtyByAmmoKey(id);

            Main.SendToAllOrServer(new AmmoDecrementPacket
            {
                SlotIndex = (int)index,
                Count = 1,
                ID = id
            });
        }
        catch (Exception ex)
        {
            AmmoPatchErrors.Report(nameof(OnAmmoSlotDecrement), ex);
        }
    }
}
