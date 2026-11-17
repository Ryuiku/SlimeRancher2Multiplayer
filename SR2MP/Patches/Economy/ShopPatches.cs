using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Shop;
using Il2CppMonomiPark.SlimeRancher.Shop.Runtime;
using SR2MP.Packets.Economy;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Economy;

// A Caretaker shop purchase. The counts are read before and after, and what changed is sent as an
// absolute count, so the others mark the item bought (or sold out) too. Money, gadgets and spawned
// items already travel through their own packets.
[HarmonyPatch(typeof(ShopDirector), nameof(ShopDirector.TrackItemPurchased))]
internal static class OnShopItemPurchased
{
    public static void Prefix(out Dictionary<string, (string Category, int Seed, string ItemId, int UnlockType, int Count)>? __state)
    {
        __state = null;

        if (HandlingPacket || !NetworkActorManager.CanSendActorChanges)
            return;

        try
        {
            __state = NetworkShopManager.CountMap();
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not read shop counts before a purchase: {ex.Message}");
        }
    }

    public static void Postfix(ShopRuntimeItem item,
        Dictionary<string, (string Category, int Seed, string ItemId, int UnlockType, int Count)>? __state)
    {
        if (__state == null || HandlingPacket || !NetworkActorManager.CanSendActorChanges)
            return;

        try
        {
            var sent = 0;
            foreach (var (key, now) in NetworkShopManager.CountMap())
            {
                if (__state.TryGetValue(key, out var before) && before.Count == now.Count && before.Seed == now.Seed)
                    continue;

                Main.SendToAllOrServer(new ShopPurchasePacket
                {
                    CategoryKey = now.Category,
                    Seed = now.Seed,
                    ItemId = now.ItemId,
                    UnlockType = now.UnlockType,
                    Count = now.Count
                });
                sent++;

                SrLogger.LogMessage($"Shop purchase sent: {now.ItemId} in {now.Category} bought {now.Count} times (seed {now.Seed})");
            }

            if (sent == 0)
            {
                string? id = null;
                try { id = item?.UniqueId?.Id; } catch { /* ignored */ }
                SrLogger.LogMessage($"Shop purchase of {id ?? "an item"} tracked but no count changed");
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a shop purchase: {ex.Message}");
        }
    }
}

// A category restocked. The host sends its new stock to everyone; a joined game keeps the
// host's seed if the host's restock already arrived.
[HarmonyPatch(typeof(ShopDataPurchaseSet), nameof(ShopDataPurchaseSet.ClearPurchases))]
internal static class OnShopRestocked
{
    public static void Postfix(ShopDataPurchaseSet __instance, int newSeed)
    {
        if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected))
            return;

        try
        {
            NetworkShopManager.OnRestocked(__instance, newSeed);
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            SrLogger.LogWarning($"Could not handle a shop restock: {ex.Message}");
        }
    }
}
