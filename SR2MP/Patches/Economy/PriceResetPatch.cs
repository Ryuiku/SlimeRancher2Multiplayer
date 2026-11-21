using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SR2MP.Packets.Economy;

namespace SR2MP.Patches.Economy;

[HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.ResetPrices))]
internal static class PriceResetPatch
{
    public static bool Prefix()
        => !Main.Client.IsConnected;

    public static void Postfix()
    {
        if (!Main.Server.IsRunning)
            return;

        var packet = new MarketPricePacket { Prices = MarketPricesArray! };
        SR2MP.Shared.Managers.MarketPriceSync.Remember(packet.Prices);

        Main.SendToAllOrServer(packet);
    }
}