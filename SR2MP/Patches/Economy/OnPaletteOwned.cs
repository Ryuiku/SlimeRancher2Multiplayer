using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Ranch;
using SR2MP.Packets.World;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.Economy;

// Every way of getting a vac palette (shop, kiosk reward, pod) ends here. Owning it counts for
// every player; which one each player picks stays their own.
[HarmonyPatch(typeof(PaletteInventoryModel), nameof(PaletteInventoryModel.SetOwned))]
internal static class OnPaletteOwned
{
    public static void Postfix(Palette palette)
    {
        // Sent even when it was not new here: it can still be new for the others.
        if (!palette || !UnlockLookup.CanSend)
            return;

        try
        {
            var id = palette.ReferenceId;
            if (string.IsNullOrEmpty(id))
                return;

            Main.SendToAllOrServer(new UnlockSharedPacket
            {
                Kind = UnlockSharedPacket.UnlockKind.Palette,
                RefId = id
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a palette: {ex.Message}");
        }
    }
}
