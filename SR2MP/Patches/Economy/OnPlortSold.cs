using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SR2MP.Packets.Economy;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Economy;

/// <summary>
/// Market saturation: the game lowers a plort's price by how many were sold, but only the host's
/// count sets the prices everyone sees. A client's sale is reported to the host, which counts it
/// with the game's own method; the host then shares any price change.
/// </summary>
[HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.RegisterSold))]
internal static class OnPlortSold
{
    // The argument names are not known: identifiable type, count, price.
    public static void Postfix(IdentifiableType __0, int __1, int __2)
    {
        try
        {
            if (Main.Server.IsRunning)
            {
                MarketPriceSync.MarkDirty();
                return;
            }

            if (HandlingPacket || !Main.Client.IsConnected || !NetworkActorManager.CanSendActorChanges || __0 == null)
                return;

            Main.Client.SendPacket(new PlortSoldPacket
            {
                Ident = NetworkActorManager.GetPersistentID(__0),
                Count = __1,
                Price = __2
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not report a plort sale: {ex.Message}");
        }
    }
}
