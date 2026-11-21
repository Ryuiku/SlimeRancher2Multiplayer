using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player;
using SR2MP.Packets.Ammo;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Ammo;

[HarmonyPatch(typeof(AmmoSlotManager), nameof(AmmoSlotManager.Decrement), typeof(int), typeof(int))]
internal static class OnAmmoDecrement
{
    public static void Postfix(AmmoSlotManager __instance, int index, int count)
    {
        if ((!Main.Client.IsConnected && !Main.Server.IsRunning) || HandlingPacket) return;

        try
        {
            var slots = __instance.Slots;
            if (slots == null || index < 0 || index >= slots.Length) return;

            var slot = slots[index];
            if (slot != null && slot._count <= 0) slot._id = null;

            var id = __instance.GetPlotID();
            if (id == null) return;

            NetworkSprinkleCanisterManager.MarkDirtyByAmmoKey(id);

            Main.SendToAllOrServer(new AmmoDecrementPacket
            {
                SlotIndex = index,
                Count = count,
                ID = id
            });
        }
        catch (Exception ex)
        {
            AmmoPatchErrors.Report(nameof(OnAmmoDecrement), ex);
        }
    }
}
