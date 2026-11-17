using Il2CppMonomiPark.SlimeRancher.Ranch;
using Il2CppMonomiPark.SlimeRancher.UI.Themes;
using SR2MP.Packets.Loading;
using SR2MP.Shared.Managers;

namespace SR2MP.Shared.Utils;

/// <summary>
/// Unlocks the game keeps per player but that count for everyone here (the host's save is the
/// source of truth): vac palettes, sanctuary themes, slime appearances, secret styles and the
/// rewards kiosk progress. Finding them by id, applying them without sending them back, and the
/// join snapshot. Which palette or appearance a player picks stays their own.
/// </summary>
internal static class UnlockLookup
{
    private static Dictionary<string, Palette>? palettes;
    private static Dictionary<string, ThemeDefinition>? themes;

    static UnlockLookup()
    {
        Main.Server.OnServerStarted +=      ClearCache;
        Main.Client.OnConnected     += _ => ClearCache();
        Main.Client.OnDisconnected  +=      ClearCache;
    }

    private static void ClearCache()
    {
        palettes = null;
        themes = null;
    }

    /// <summary>
    /// Whether a local unlock may be told to the others: not while applying someone else's, and
    /// not while a joining player's game still hands out what their own save has.
    /// </summary>
    internal static bool CanSend => !HandlingPacket && NetworkActorManager.CanSendActorChanges;

    #region Lookups

    // Keyed by the object's own reference id, so it does not matter what the save table uses as key.
    internal static Palette? FindPalette(string refId)
    {
        if (string.IsNullOrEmpty(refId))
            return null;

        if (palettes == null || palettes.Count == 0)
        {
            var map = new Dictionary<string, Palette>();
            try
            {
                foreach (var entry in GameContext.Instance.AutoSaveDirector._saveReferenceTranslation
                             ._paletteTranslation.RawLookupDictionary)
                    AddPalette(map, entry.Value);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Palette lookup from the save table failed: {ex.Message}");
            }

            try
            {
                foreach (var palette in SceneContext.Instance.PaletteDirector._palettes.items)
                    AddPalette(map, palette);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Palette lookup from the director failed: {ex.Message}");
            }

            palettes = map;
        }

        return palettes.TryGetValue(refId, out var found) && found ? found : null;
    }

    private static void AddPalette(Dictionary<string, Palette> map, Palette? palette)
    {
        if (!palette)
            return;

        var id = palette!.ReferenceId;
        if (!string.IsNullOrEmpty(id))
            map.TryAdd(id, palette);
    }

    internal static ThemeDefinition? FindTheme(string refId)
    {
        if (string.IsNullOrEmpty(refId))
            return null;

        if (themes == null || themes.Count == 0)
        {
            var map = new Dictionary<string, ThemeDefinition>();
            try
            {
                foreach (var entry in GameContext.Instance.AutoSaveDirector._saveReferenceTranslation
                             ._themeDefinitionTranslation.RawLookupDictionary)
                {
                    var theme = entry.Value;
                    if (theme && !string.IsNullOrEmpty(theme.ReferenceId))
                        map.TryAdd(theme.ReferenceId, theme);
                }
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Theme lookup failed: {ex.Message}");
            }

            themes = map;
        }

        return themes.TryGetValue(refId, out var found) && found ? found : null;
    }

    internal static IdentifiableType? FindIdent(int id)
        => id >= 0 && ActorManager.ActorTypes.TryGetValue(id, out var type) && type ? type : null;

    #endregion

    #region Applying (from the network: nothing is sent back)

    internal static bool ApplyPalette(string refId)
    {
        var palette = FindPalette(refId);
        if (palette == null)
        {
            SrLogger.LogDebug($"Palette {refId} is unknown here; skipped");
            return false;
        }

        var inventory = SceneContext.Instance.PlayerState._model.PaletteInventory;
        if (inventory.IsOwned(palette))
            return false;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            // Through the director, so whatever it updates along with the model runs too.
            try
            {
                SceneContext.Instance.PaletteDirector.SetOwned(palette);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Palette director refused {refId}, writing the model: {ex.Message}");
            }

            if (!inventory.IsOwned(palette))
                inventory.SetOwned(palette);
        }
        finally
        {
            HandlingPacket = wasHandling;
        }

        return true;
    }

    internal static bool ApplyTheme(string refId, bool owned)
    {
        var theme = FindTheme(refId);
        if (theme == null)
        {
            SrLogger.LogDebug($"Sanctuary theme {refId} is unknown here; skipped");
            return false;
        }

        var inventory = SceneContext.Instance.PlayerState._model.SanctuaryThemeInventory;
        if (inventory.IsThemeOwned(theme) == owned)
            return false;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            if (owned)
            {
                try
                {
                    SceneContext.Instance.SanctuaryDirector.SetThemeOwned(theme);
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Sanctuary director refused theme {refId}, writing the model: {ex.Message}");
                }

                if (!inventory.IsThemeOwned(theme))
                    inventory.SetThemeOwned(theme);
            }
            else
            {
                inventory.SetThemeUnowned(theme);
            }
        }
        finally
        {
            HandlingPacket = wasHandling;
        }

        return true;
    }

    internal static bool UnlockAppearance(int slimeId, SlimeAppearance.AppearanceSaveSet saveSet)
    {
        var type = FindIdent(slimeId);
        if (type == null)
            return false;

        var slime = type.TryCast<SlimeDefinition>();
        var director = SceneContext.Instance.SlimeAppearanceDirector;
        if (!slime || !director)
            return false;

        var appearance = director.GetSpecificSlimeAppearance(type, saveSet);
        if (!appearance || director.IsAppearanceUnlocked(slime, appearance))
            return false;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            director.UnlockAppearance(slime, appearance);
        }
        finally
        {
            HandlingPacket = wasHandling;
        }

        return true;
    }

    internal static bool DiscoverSecretStyle(int slimeId)
    {
        var slime = FindIdent(slimeId)?.TryCast<SlimeDefinition>();
        var model = SceneContext.Instance.SlimeAppearanceDirector?.secretStyleDiscoveriesModel;
        if (!slime || model == null || model.IsDiscovered(slime))
            return false;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            return model.Discover(slime);
        }
        finally
        {
            HandlingPacket = wasHandling;
        }
    }

    #endregion

    #region Rewards kiosk

    internal static void ApplyRewardProgress(
        SR2MP.Packets.World.EndGameRewardProgressPacket.ProgressOp op, string rewardId, int identId, int count)
    {
        if (string.IsNullOrEmpty(rewardId))
            return;

        var model = SceneContext.Instance.PlayerState._model.EndGameRewards;
        SR2MP.Patches.Economy.OnKioskDispense.LastRemoteProgress = UnityEngine.Time.time;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            switch (op)
            {
                case SR2MP.Packets.World.EndGameRewardProgressPacket.ProgressOp.ClearReward:
                    model.ClearRewardProgress(rewardId);
                    break;

                case SR2MP.Packets.World.EndGameRewardProgressPacket.ProgressOp.ClearItem:
                {
                    var type = FindIdent(identId);
                    if (type != null)
                        model.ClearItemProgress(rewardId, type);
                    break;
                }

                default:
                {
                    var type = FindIdent(identId);
                    if (type == null)
                    {
                        SrLogger.LogDebug($"Rewards kiosk item {identId} is unknown here; skipped");
                        break;
                    }

                    if (model.GetItemProgressOrDefault(rewardId, type) != count)
                        model.SetItemProgress(rewardId, type, count);
                    break;
                }
            }
        }
        finally
        {
            HandlingPacket = wasHandling;
        }
    }

    /// <summary>
    /// Host: a client's count for a kiosk item, made from a count the host no longer has (another
    /// player deposited the same item at about the same time). Both games sent "old + 1", so one
    /// deposit was lost in every save. The client's change is added to the host's count instead,
    /// and the result goes to everyone, the sender included. False when there was no clash.
    /// </summary>
    internal static bool MergeConcurrentRewardProgress(SR2MP.Packets.World.EndGameRewardProgressPacket packet)
    {
        if (packet.Op != SR2MP.Packets.World.EndGameRewardProgressPacket.ProgressOp.Set
            || packet.Previous < 0 || string.IsNullOrEmpty(packet.RewardId))
            return false;

        var type = FindIdent(packet.IdentId);
        if (type == null)
            return false;

        var model = SceneContext.Instance.PlayerState._model.EndGameRewards;
        var current = model.GetItemProgressOrDefault(packet.RewardId, type);
        if (current == packet.Previous)
            return false;

        var merged = Math.Max(0, current + packet.Count - packet.Previous);
        SrLogger.LogMessage($"Rewards kiosk {packet.RewardId}: a player's count {packet.Previous} -> {packet.Count} met {current} here; now {merged}");

        ApplyRewardProgress(packet.Op, packet.RewardId, packet.IdentId, merged);

        Main.SendToAllOrServer(new SR2MP.Packets.World.EndGameRewardProgressPacket
        {
            RewardId = packet.RewardId,
            IdentId = packet.IdentId,
            Count = merged,
            Op = SR2MP.Packets.World.EndGameRewardProgressPacket.ProgressOp.Set,
            Previous = -1
        });
        return true;
    }

    /// <summary>The host's kiosk progress replaces this save's.</summary>
    private static void ApplyRewardSnapshot(List<(string RewardId, int IdentId, int Count)> entries)
    {
        var model = SceneContext.Instance.PlayerState._model.EndGameRewards;
        var hostRewards = new Dictionary<string, Dictionary<int, int>>();
        foreach (var (rewardId, identId, count) in entries)
        {
            if (string.IsNullOrEmpty(rewardId))
                continue;

            if (!hostRewards.TryGetValue(rewardId, out var items))
                hostRewards[rewardId] = items = new Dictionary<int, int>();
            items[identId] = count;
        }

        SR2MP.Patches.Economy.OnKioskDispense.LastRemoteProgress = UnityEngine.Time.time;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            // Keys first: the game's dictionaries change while we clear.
            var localRewards = new List<string>();
            foreach (var entry in model.RewardsProgress)
                localRewards.Add(entry.Key);

            foreach (var rewardId in localRewards)
            {
                if (!hostRewards.TryGetValue(rewardId, out var hostItems))
                {
                    model.ClearRewardProgress(rewardId);
                    continue;
                }

                if (!model.RewardsProgress.TryGetValue(rewardId, out var progress) || progress == null)
                    continue;

                var localItems = new List<IdentifiableType>();
                foreach (var item in progress.CountsCollected)
                    localItems.Add(item.Key);

                foreach (var item in localItems)
                {
                    if (item && !hostItems.ContainsKey(NetworkActorManager.GetPersistentID(item)))
                        model.ClearItemProgress(rewardId, item);
                }
            }

            foreach (var (rewardId, items) in hostRewards)
            {
                foreach (var (identId, count) in items)
                {
                    var type = FindIdent(identId);
                    if (type != null && model.GetItemProgressOrDefault(rewardId, type) != count)
                        model.SetItemProgress(rewardId, type, count);
                }
            }
        }
        finally
        {
            HandlingPacket = wasHandling;
        }
    }

    #endregion

    #region Join snapshot

    internal static InitialUnlocksPacket CreateInitialPacket()
    {
        var packet = new InitialUnlocksPacket();
        var player = SceneContext.Instance.PlayerState._model;

        try
        {
            foreach (var entry in player.PaletteInventory._ownedPalettes)
            {
                var id = entry.Value ? entry.Value.ReferenceId : null;
                if (!string.IsNullOrEmpty(id))
                    packet.PaletteRefIds.Add(id!);
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not read owned palettes: {ex.Message}");
        }

        try
        {
            foreach (var theme in player.SanctuaryThemeInventory._ownedThemes)
            {
                if (theme && !string.IsNullOrEmpty(theme.ReferenceId))
                    packet.ThemeRefIds.Add(theme.ReferenceId);
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not read owned sanctuary themes: {ex.Message}");
        }

        var director = SceneContext.Instance.SlimeAppearanceDirector;
        try
        {
            foreach (var entry in director.appearanceSelections._unlocks)
            {
                if (!entry.Key || entry.Value == null)
                    continue;

                var slimeId = NetworkActorManager.GetPersistentID(entry.Key);
                foreach (var appearance in entry.Value)
                {
                    if (appearance)
                        packet.Appearances.Add((slimeId, (byte)appearance.SaveSet));
                }
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not read unlocked slime appearances: {ex.Message}");
        }

        try
        {
            foreach (var type in director.secretStyleDiscoveriesModel.discoveredSecretStyles)
            {
                if (type)
                    packet.SecretStyles.Add(NetworkActorManager.GetPersistentID(type));
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not read discovered secret styles: {ex.Message}");
        }

        try
        {
            foreach (var reward in player.EndGameRewards.RewardsProgress)
            {
                if (string.IsNullOrEmpty(reward.Key) || reward.Value == null)
                    continue;

                foreach (var item in reward.Value.CountsCollected)
                {
                    if (item.Key)
                        packet.RewardProgress.Add((reward.Key, NetworkActorManager.GetPersistentID(item.Key), item.Value));
                }
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not read rewards kiosk progress: {ex.Message}");
        }

        return packet;
    }

    internal static void ApplyInitialPacket(InitialUnlocksPacket packet)
    {
        int palettesAdded = 0, themesAdded = 0, appearancesAdded = 0, stylesAdded = 0;

        // Each step on its own, so one failure does not lose the rest. Added to what the joiner
        // owns; nothing they already have is taken away.
        foreach (var id in packet.PaletteRefIds)
            Try(() => { if (ApplyPalette(id)) palettesAdded++; }, $"palette {id}");

        foreach (var id in packet.ThemeRefIds)
            Try(() => { if (ApplyTheme(id, true)) themesAdded++; }, $"sanctuary theme {id}");

        foreach (var (slimeId, saveSet) in packet.Appearances)
            Try(() => { if (UnlockAppearance(slimeId, (SlimeAppearance.AppearanceSaveSet)saveSet)) appearancesAdded++; },
                $"appearance {slimeId}/{saveSet}");

        foreach (var slimeId in packet.SecretStyles)
            Try(() => { if (DiscoverSecretStyle(slimeId)) stylesAdded++; }, $"secret style {slimeId}");

        Try(() => ApplyRewardSnapshot(packet.RewardProgress), "rewards kiosk progress");

        SrLogger.LogMessage(
            $"Unlocks from the host: {palettesAdded}/{packet.PaletteRefIds.Count} palettes, " +
            $"{themesAdded}/{packet.ThemeRefIds.Count} themes, {appearancesAdded}/{packet.Appearances.Count} appearances, " +
            $"{stylesAdded}/{packet.SecretStyles.Count} secret styles were new here; " +
            $"{packet.RewardProgress.Count} kiosk progress entries applied.");
    }

    private static void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply the host's {what}: {ex.Message}");
        }
    }

    #endregion
}
