using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Shop;
using SR2MP.Packets.Economy;
using SR2MP.Shared.Utils;

namespace SR2MP.Shared.Managers;

/// <summary>
/// The Caretaker shop. Each game picks its stock from a seed per category and counts its own
/// purchases, so players saw different stock and could each buy a limited item once. Here the
/// host's seeds and counts are used by everyone, and purchases by anyone are counted for all.
/// </summary>
internal static class NetworkShopManager
{
    // The host restocked; its whole shop goes out at the next flush.
    private static bool dirty;
    private static float lastFlush;
    private const float FlushInterval = 1f;

    // On a joined game: the seed the host last sent per category, and when. A restock this game
    // runs itself shortly after is the same restock, and takes the host's seed.
    private static readonly Dictionary<string, (int Seed, float At)> HostSeeds = new();
    private const float SameRestockWindow = 120f;

    static NetworkShopManager()
    {
        Main.Server.OnServerStarted +=      ClearSession;
        Main.Client.OnConnected     += _ => ClearSession();
        Main.Client.OnDisconnected  +=      ClearSession;
    }

    private static void ClearSession()
    {
        dirty = false;
        HostSeeds.Clear();
    }

    private static ShopDataModel? Model => SceneContext.Instance?.ShopDirector?._dataModel;

    internal static string EntryKey(string category, string itemId, int unlockType) => $"{category}|{itemId}|{unlockType}";

    #region Reading

    internal static List<ShopStatePacket.Set> Snapshot()
    {
        var result = new List<ShopStatePacket.Set>();
        var sets = Model?._shopPurchasesByCategory;
        if (sets == null)
            return result;

        foreach (var pair in sets)
        {
            var set = pair.Value;
            if (string.IsNullOrEmpty(pair.Key) || set == null)
                continue;

            var snapshot = new ShopStatePacket.Set { Key = pair.Key, Seed = set._seed };

            var purchases = set._purchases;
            if (purchases != null)
            {
                foreach (var entry in purchases)
                {
                    if (entry == null)
                        continue;

                    var itemId = ReadItemId(entry);
                    if (string.IsNullOrEmpty(itemId))
                        continue;

                    snapshot.Entries.Add(new ShopStatePacket.Entry
                    {
                        ItemId = itemId!,
                        UnlockType = (int)entry._unlockType,
                        Count = entry._count
                    });
                }
            }

            result.Add(snapshot);
        }

        return result;
    }

    /// <summary>
    /// Purchase counts by entry key, for spotting what one purchase changed.
    /// </summary>
    internal static Dictionary<string, (string Category, int Seed, string ItemId, int UnlockType, int Count)> CountMap()
    {
        var map = new Dictionary<string, (string, int, string, int, int)>();
        foreach (var set in Snapshot())
        {
            foreach (var entry in set.Entries)
                map[EntryKey(set.Key, entry.ItemId, entry.UnlockType)] = (set.Key, set.Seed, entry.ItemId, entry.UnlockType, entry.Count);
        }

        return map;
    }

    private static string? ReadItemId(ShopDataPurchaseEntry entry)
    {
        try
        {
            return entry._uniqueId?.Id;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Writing

    private static ShopDataPurchaseSet? GetOrCreateSet(string key, int seed, bool create)
    {
        var sets = Model?._shopPurchasesByCategory;
        if (sets == null)
            return null;

        if (sets.TryGetValue(key, out var set) && set != null)
            return set;

        if (!create)
            return null;

        set = new ShopDataPurchaseSet
        {
            _categoryIdentifier = key,
            _seed = seed,
            _purchases = new CppCollections.List<ShopDataPurchaseEntry>()
        };
        sets[key] = set;
        return set;
    }

    private static ShopDataPurchaseEntry CreateEntry(string itemId, int unlockType, int count) => new()
    {
        _uniqueId = new ShopItemId(itemId),
        _count = count,
        _unlockType = (AcquireGadgetType)unlockType
    };

    /// <summary>
    /// Replaces this game's seeds and purchase counts with the host's. Returns whether anything changed.
    /// </summary>
    internal static bool ApplySnapshot(List<ShopStatePacket.Set> sets)
    {
        var changed = false;
        var now = UnityEngine.Time.realtimeSinceStartup;

        HandlingPacket = true;
        try
        {
            foreach (var hostSet in sets)
            {
                try
                {
                    if (string.IsNullOrEmpty(hostSet.Key))
                        continue;

                    HostSeeds[hostSet.Key] = (hostSet.Seed, now);

                    var set = GetOrCreateSet(hostSet.Key, hostSet.Seed, true);
                    if (set == null)
                        return changed;

                    if (set._seed != hostSet.Seed)
                    {
                        SrLogger.LogMessage($"Shop category {hostSet.Key}: seed {set._seed} -> host's {hostSet.Seed}");
                        set._seed = hostSet.Seed;
                        changed = true;
                    }

                    if (!SameEntries(set, hostSet))
                    {
                        var purchases = new CppCollections.List<ShopDataPurchaseEntry>();
                        foreach (var entry in hostSet.Entries)
                        {
                            if (!string.IsNullOrEmpty(entry.ItemId))
                                purchases.Add(CreateEntry(entry.ItemId, entry.UnlockType, entry.Count));
                        }

                        set._purchases = purchases;
                        changed = true;
                    }
                }
                catch (Exception ex)
                {
                    SrLogger.LogWarning($"Could not apply shop category {hostSet.Key}: {ex.Message}");
                }
            }
        }
        finally
        {
            HandlingPacket = false;
        }

        SrLogger.LogMessage($"Applied the host's shop: {sets.Count} categories{(changed ? ", stock or counts changed" : ", already the same")}.");
        return changed;
    }

    private static bool SameEntries(ShopDataPurchaseSet set, ShopStatePacket.Set hostSet)
    {
        var local = new Dictionary<string, int>();
        var purchases = set._purchases;
        if (purchases != null)
        {
            foreach (var entry in purchases)
            {
                var id = entry != null ? ReadItemId(entry) : null;
                if (!string.IsNullOrEmpty(id))
                    local[$"{id}|{(int)entry!._unlockType}"] = entry._count;
            }
        }

        if (local.Count != hostSet.Entries.Count)
            return false;

        foreach (var entry in hostSet.Entries)
        {
            if (!local.TryGetValue($"{entry.ItemId}|{entry.UnlockType}", out var count) || count != entry.Count)
                return false;
        }

        return true;
    }

    /// <summary>
    /// One purchase count from another player. Used only when both games show the same stock
    /// (same seed); the count is absolute and never lowered. Returns whether anything changed.
    /// </summary>
    internal static bool ApplyEntry(string key, int seed, string itemId, int unlockType, int count)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(itemId))
            return false;

        try
        {
            // The host's save is the one that keeps purchases. A category the host has never
            // bought from has no stock record yet; dropping a client's purchase there meant the
            // host's save never kept it. The sender's stock becomes the host's (and everyone's).
            var set = GetOrCreateSet(key, seed, false);
            if (set == null && Main.Server.IsRunning)
            {
                set = GetOrCreateSet(key, seed, true);
                if (set != null)
                {
                    SrLogger.LogMessage($"Shop category {key}: no stock here yet, the buyer's (seed {seed}) is kept");
                    dirty = true;
                }
            }

            if (set == null || set._seed != seed)
            {
                SrLogger.LogMessage($"Shop purchase of {itemId} in {key} is for another stock (seed {seed}, here {set?._seed.ToString() ?? "none"}), ignored");

                // The host's stock is the one that counts: send it out again so everyone matches.
                if (Main.Server.IsRunning)
                    dirty = true;

                return false;
            }

            HandlingPacket = true;
            try
            {
                set._purchases ??= new CppCollections.List<ShopDataPurchaseEntry>();

                foreach (var entry in set._purchases)
                {
                    if (entry == null || (int)entry._unlockType != unlockType || ReadItemId(entry) != itemId)
                        continue;

                    if (entry._count >= count)
                        return false;

                    entry._count = count;
                    SrLogger.LogMessage($"Shop purchase: {itemId} in {key} now bought {count} times");
                    return true;
                }

                set._purchases.Add(CreateEntry(itemId, unlockType, count));
                SrLogger.LogMessage($"Shop purchase: {itemId} in {key} now bought {count} times");
                return true;
            }
            finally
            {
                HandlingPacket = false;
            }
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            SrLogger.LogWarning($"Could not apply a shop purchase of {itemId} in {key}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Makes the shop pick its items again from the model. An open shop screen may only show the
    /// change after it is reopened.
    /// </summary>
    internal static void Refresh()
    {
        var runtimes = SceneContext.Instance?.ShopDirector?._runtimes;
        if (runtimes == null)
            return;

        foreach (var runtime in runtimes)
        {
            if (runtime == null)
                continue;

            CppCollections.List<Il2CppMonomiPark.SlimeRancher.Shop.Runtime.ShopCategoryRuntime>? categories;
            try
            {
                categories = Il2CppSystem.Linq.Enumerable.ToList(runtime.AllCategories);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Could not list shop categories: {ex.Message}");
                continue;
            }

            foreach (var category in categories)
            {
                if (category == null)
                    continue;

                var before = category._lastSeedUsed;

                try
                {
                    category.ClearCachedItems();
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Could not clear shop category {category.AnalyticsName}: {ex.Message}");
                }

                try
                {
                    category.RepopulateImmediately();
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Could not refill shop category {category.AnalyticsName}: {ex.Message}");
                }

                SrLogger.LogDebug($"Shop category {category.AnalyticsName}: seed used {before} -> {category._lastSeedUsed}");
            }
        }
    }

    #endregion

    #region Restocks

    /// <summary>
    /// A category was restocked here with a new seed.
    /// </summary>
    internal static void OnRestocked(ShopDataPurchaseSet set, int newSeed)
    {
        var key = set._categoryIdentifier ?? string.Empty;

        if (Main.Server.IsRunning)
        {
            SrLogger.LogMessage($"Shop category {key} restocked with seed {newSeed}, sending it to everyone");
            dirty = true;
            return;
        }

        if (!Main.Client.IsConnected)
            return;

        // The same restock the host already sent: keep the host's stock.
        if (HostSeeds.TryGetValue(key, out var host)
            && UnityEngine.Time.realtimeSinceStartup - host.At < SameRestockWindow
            && host.Seed != set._seed)
        {
            HandlingPacket = true;
            try
            {
                set._seed = host.Seed;
            }
            finally
            {
                HandlingPacket = false;
            }

            SrLogger.LogMessage($"Shop category {key} restocked here with seed {newSeed}; keeping the host's {host.Seed}");
            if (MainThreadDispatcher.Instance)
                MainThreadDispatcher.Instance.Enqueue(Refresh);
            return;
        }

        SrLogger.LogMessage($"Shop category {key} restocked here with seed {newSeed}; waiting for the host's");
    }

    /// <summary>
    /// Sends the host's whole shop when it changed. Called every frame on the main thread.
    /// </summary>
    internal static void Tick()
    {
        if (!dirty || !Main.Server.IsRunning)
            return;

        var now = UnityEngine.Time.realtimeSinceStartup;
        if (now - lastFlush < FlushInterval)
            return;

        lastFlush = now;
        dirty = false;

        var packet = CreateStatePacket();
        if (packet != null)
            Main.Server.SendToAll(packet);
    }

    internal static ShopStatePacket? CreateStatePacket()
    {
        try
        {
            return new ShopStatePacket { Sets = Snapshot() };
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not read the shop: {ex.Message}");
            return null;
        }
    }

    #endregion
}
