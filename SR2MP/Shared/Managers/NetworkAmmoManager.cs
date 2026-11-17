using System.Collections;
using Il2CppMonomiPark.SlimeRancher.Caretaker;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Ranch;
using SR2MP.Packets.Ammo;
using SR2MP.Shared.Utils;
// ReSharper disable InconsistentNaming

namespace SR2MP.Shared.Managers;

internal static class NetworkAmmoManager
{
    public static void Initialize()
    {
        ClearAmmoCache();
        slotDefinitions.Clear();

        // Right now, I don't know where the definitions are actually stored,
        // but it isn't in the normal places like LookupDirector or SaveReferenceTranslation.
        //
        // However, I can see that only the definitions for plots or gadgets are loaded on the main menu,
        // so they are probably just stored where they are used only.
        // -PinkTarr
        foreach (var def in Resources.FindObjectsOfTypeAll<AmmoSlotDefinition>())
            slotDefinitions[def.name.Hash16()] = def;

        LearnAmmoSetGuids();
    }

    public static int GetNextSlot(this AmmoSlotManager ammo, IdentifiableType id)
    {
        for (var i = 0; i < ammo._ammoModel.Slots.Count; i++)
        {
            var isSlotEmptyOrSameType = ammo.Slots[i]!._count == 0 || ammo.Slots[i]!._id == id;

            var isSlotFull = ammo.Slots[i]!.Count >= ammo.Slots[i]!.MaxCount;

            if (isSlotEmptyOrSameType && isSlotFull) break;

            if (isSlotEmptyOrSameType)
                return i;
        }

        return -1;
    }
    
    public static void ApplySlotData(this AmmoSlotManager ammo, NetworkAmmo networkAmmo)
    {
        // The host leaves out slots it cannot describe, so its slot numbers can have gaps (0, 2, 3)
        // while ToGameAmmo builds the slots one after another. Using the host's numbers here ran
        // past the end of the storage and the whole plot failed to load.
        var position = 0;
        foreach (var (_, networkSlot) in networkAmmo.AmmoSlots)
        {
            var index = position++;

            var slots = ammo.Slots;
            if (slots != null && index < slots.Length)
            {
                var ammoSlot = slots[index];
                if (ammoSlot != null)
                {
                    ammoSlot.Count = networkSlot.Count;
                    ammoSlot.MaxCount = networkSlot.MaxCount;
                }
            }

            var modelSlots = ammo._ammoModel?.Slots;
            if (modelSlots != null && index < modelSlots.Length)
            {
                var ammoModelSlot = modelSlots[index];
                if (ammoModelSlot != null)
                {
                    ammoModelSlot.Count = networkSlot.Count;
                    ammoModelSlot.MaxCount = networkSlot.MaxCount;
                }
            }
        }
    }
    
    private static readonly Dictionary<ushort, AmmoSlotDefinition> slotDefinitions = new();
    private static readonly Dictionary<IntPtr, string> ammoToID = new();
    private static readonly Dictionary<string, AmmoSlotManager> IDToAmmo = new();
    private static readonly Dictionary<IntPtr, (AmmoSlotManager ammo, int index)> slotToAmmo = new();

    private static readonly Dictionary<long, AmmoSlotManager> warpDepotAmmoByActorId = new();

    public static string? GetPlotID(this AmmoSlotManager ammo) => ammoToID.GetValueOrDefault(ammo.Pointer);

    public static string? GetPlotID(this AmmoSlot slot)
        => slotToAmmo.TryGetValue(slot.Pointer, out var ammoTuple) ? ammoTuple.ammo.GetPlotID() : null;

    public static int? GetSlotIndex(this AmmoSlot slot)
    {
        if (slotToAmmo.TryGetValue(slot.Pointer, out var ammoTuple))
            return ammoTuple.index;

        return null;
    }

    // public static AmmoSlotManager? GetAmmo(this AmmoSlot slot)
    //     => slotToAmmo.TryGetValue(slot.Pointer, out var ammoTuple) ? ammoTuple.ammo : null;

    public static AmmoSlotManager? GetAmmo(string? id) => IDToAmmo!.GetValueOrDefault(id);

    private static void ClearAmmoCache()
    {
        ammoToID.Clear();
        IDToAmmo.Clear();
        slotToAmmo.Clear();
        warpDepotAmmoByActorId.Clear();
    }

    internal static void RegisterAmmoPointer(this AmmoSlotManager ammo, string id)
    {
        ammoToID[ammo.Pointer] = id;
        IDToAmmo[id] = ammo;

        for (var i = 0; i < ammo.Slots.Count; i++)
        {
            var slot = ammo.Slots[i];
            slotToAmmo[slot!.Pointer] = (ammo, i);
        }
    }

    public static void RegisterAmmoPointer(this SiloStorage siloStorage)
    {
        StartCoroutine(RegisterAmmoPointerCoroutine(siloStorage));
    }

    private static IEnumerator RegisterAmmoPointerCoroutine(SiloStorage siloStorage)
    {
        yield return new WaitFrames(3);
        
        if (siloStorage == null) yield break;

        // needs to include inactive ones, don't question why
        var plot = siloStorage.GetComponentInParent<LandPlotLocation>(true);
        var gadget = siloStorage.GetComponentInParent<Gadget>(true);
        var sprinkle = siloStorage.GetComponentInParent<SprinkleCanister>(true);

        if (plot == null && gadget == null && sprinkle == null)
        {
            SrLogger.LogWarning($"SiloStorage has no known parent type: {siloStorage.name}");
            yield break;
        }

        var ammo = siloStorage.GetRelevantAmmo();
        if (ammo == null)
        {
            SrLogger.LogWarning($"SiloStorage has no ammo to register yet: {siloStorage.name}");
            yield break;
        }

        if (plot != null)
        {
            LearnAmmoSetGuid(siloStorage.AmmoSetReference);
            ammo.RegisterAmmoPointer($"{plot._id}_{siloStorage.AmmoSetReference.name}");
            yield break;
        }

        if (gadget != null)
        {
            var actorId = gadget.GetActorId().Value;
            
            if (siloStorage.TryCast<LinkedSiloStorage>() != null)
            {
                RegisterWarpDepotAmmo(ammo, actorId);
                yield break;
            }

            if (siloStorage.AmmoSetReference == null)
            {
                SrLogger.LogWarning($"SiloStorage has no AmmoSetReference yet: {siloStorage.name}");
                yield break;
            }

            ammo.RegisterAmmoPointer($"gadget{actorId}_{siloStorage.AmmoSetReference.name}");
            yield break;
        }

        if (sprinkle != null)
        {
            // The canister's own id, the same on every machine. Its IdHandler is not always on the
            // same object and it may have no ammo set reference; both used to throw here and leave
            // the canister out of sync. Its SetModel patch registers it the same way.
            var key = NetworkSprinkleCanisterManager.AmmoKey(sprinkle);
            if (key == null)
            {
                SrLogger.LogWarning($"Sprinkle canister has no id yet: {siloStorage.name}");
                yield break;
            }

            ammo.RegisterAmmoPointer(key);
        }
    }

    private static void RegisterWarpDepotAmmo(AmmoSlotManager ammo, long actorId)
    {
        warpDepotAmmoByActorId[actorId] = ammo;

        var ammoId = NetworkGadgetManager.TryGetLinkedGadgetId(actorId, out var partnerId)
            ? Math.Min(actorId, partnerId)
            : actorId;

        ammo.RegisterAmmoPointer($"gadget{ammoId}_warpdepot");
    }
    
    internal static void OnGadgetLinkResolved(long gadgetId, long partnerId)
    {
        var ammoId = Math.Min(gadgetId, partnerId);
        ReKeyWarpDepotAmmo(gadgetId, ammoId);
        ReKeyWarpDepotAmmo(partnerId, ammoId);
    }

    private static void ReKeyWarpDepotAmmo(long actorId, long ammoId)
    {
        if (!warpDepotAmmoByActorId.TryGetValue(actorId, out var ammo) || ammo == null)
            return;

        var newId = $"gadget{ammoId}_warpdepot";

        if (ammoToID.TryGetValue(ammo.Pointer, out var currentId))
        {
            if (currentId == newId) return;
            IDToAmmo.Remove(currentId);
        }

        ammo.RegisterAmmoPointer(newId);
    }

    public static AmmoSlotDefinition GetSlotDefinition(ushort id) => slotDefinitions[id];

    public static ushort GetId(AmmoSlotDefinition def)
    {
        if (def.name == null)
        {
            SrLogger.LogError("GetId called with a null definition name.");
            return 0;
        }

        var hash = def.name.Hash16();
        slotDefinitions.TryAdd(hash, def);
        return hash;
    }

    #region Storage that is not loaded here

    // The ammo packets name a plot's storage by its ammo set's name, but the plot model keeps its
    // storages by the set's Guid. Not every set is loaded on the main menu, so sets are also
    // learned when a storage registers, and looked for again once when a name is still unknown.
    private static readonly Dictionary<string, string> ammoSetGuidByName = new();
    private static readonly HashSet<string> rescannedAmmoSetNames = new();
    private static readonly HashSet<string> loggedUnknownStorages = new();

    private static void LearnAmmoSetGuids()
    {
        try
        {
            foreach (var def in Resources.FindObjectsOfTypeAll<PlotAmmoSetDefinition>())
                LearnAmmoSetGuid(def);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not list the plot ammo sets: {ex.Message}");
        }
    }

    private static void LearnAmmoSetGuid(PlotAmmoSetDefinition? def)
    {
        if (def == null || string.IsNullOrEmpty(def.name) || string.IsNullOrEmpty(def.Guid))
            return;

        ammoSetGuidByName[def.name] = def.Guid;
    }

    private static string? GuidForAmmoSet(string name)
    {
        if (ammoSetGuidByName.TryGetValue(name, out var guid))
            return guid;

        if (rescannedAmmoSetNames.Add(name))
        {
            LearnAmmoSetGuids();
            if (ammoSetGuidByName.TryGetValue(name, out guid))
                return guid;
        }

        return null;
    }

    /// <summary>
    /// The saved contents of a storage that has no live object on this machine (its ranch area or
    /// zone is not loaded, or it has not registered yet). Null when the id is not a land plot's
    /// storage or the plot has no such storage.
    /// </summary>
    internal static AmmoModel? GetModelAmmo(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        // Sprinkle canisters: their producer sends the whole canister soon after any change
        // (NetworkSprinkleCanisterManager), which also writes it when the Sanctuary is not loaded.
        if (id.StartsWith(NetworkSprinkleCanisterManager.AmmoKeyPrefix, StringComparison.Ordinal))
            return null;

        var plots = SceneContext.Instance ? SceneContext.Instance.GameModel?.landPlots : null;
        if (plots == null)
            return null;

        // Plot ids can contain '_' themselves, so the longest plot id that fits wins.
        string? plotId = null;
        LandPlotModel? model = null;
        foreach (var pair in plots)
        {
            var key = pair.key;
            if (string.IsNullOrEmpty(key) || key.Length + 1 >= id.Length)
                continue;
            if (id[key.Length] != '_' || !id.StartsWith(key, StringComparison.Ordinal))
                continue;
            if (plotId != null && plotId.Length >= key.Length)
                continue;

            plotId = key;
            model = pair.value;
        }

        // Gadget storages (gadget{id}_..., warp depots) have no land plot model; their models
        // belong to the gadgets and are not written here.
        if (plotId == null || model?.siloAmmo == null)
            return null;

        var setName = id.Substring(plotId.Length + 1);
        var guid = GuidForAmmoSet(setName);
        if (guid != null && model.siloAmmo.TryGetValue(guid, out var ammo) && ammo != null)
            return ammo;

        // A plot with only one storage can only mean that one.
        if (guid == null && model.siloAmmo.Count == 1)
        {
            foreach (var pair in model.siloAmmo)
                return pair.value;
        }

        if (loggedUnknownStorages.Add(id))
            SrLogger.LogDebug($"Storage '{id}' is not loaded here and its saved contents were not found (unknown ammo set '{setName}', {model.siloAmmo.Count} storages on the plot)");

        return null;
    }

    // The packets below change a storage that only exists as saved data here. Nothing listens to
    // that data while the storage is not loaded, so the slots are written directly; when the area
    // loads, the storage is built from them. This keeps the host's save and the next join right
    // when the host was away while someone else used the storage.

    internal static void TryApplyAddToModel(string? id, IdentifiableType ident, int count)
    {
        try
        {
            var model = GetModelAmmo(id);
            var slots = model?.Slots;
            if (slots == null)
                return;

            var index = NextModelSlot(slots, ident);
            if (index == -1)
                return;

            AddToModelSlot(slots[index]!, ident, count, id);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not add to the saved storage '{id}': {ex.Message}");
        }
    }

    internal static void TryApplyAddToSlotModel(string? id, int slotIndex, IdentifiableType ident, int count)
    {
        try
        {
            var slots = GetModelAmmo(id)?.Slots;
            if (slots == null || slotIndex < 0 || slotIndex >= slots.Length || slots[slotIndex] == null)
                return;

            AddToModelSlot(slots[slotIndex]!, ident, count, id);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not add to slot {slotIndex} of the saved storage '{id}': {ex.Message}");
        }
    }

    internal static void TryApplyDecrementToModel(string? id, int slotIndex, int count)
    {
        try
        {
            var slots = GetModelAmmo(id)?.Slots;
            if (slots == null || slotIndex < 0 || slotIndex >= slots.Length)
                return;

            var slot = slots[slotIndex];
            if (slot == null)
                return;

            slot._count = Math.Max(0, slot._count - count);
            if (slot._count == 0)
                slot._id = null;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not take from slot {slotIndex} of the saved storage '{id}': {ex.Message}");
        }
    }

    // The same choice as GetNextSlot: a slot with the same item that is not full, else the first
    // empty one; a full slot of that item means there is no room.
    private static int NextModelSlot(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AmmoSlot> slots, IdentifiableType ident)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            var emptyOrSame = slot._count == 0 || slot._id == ident;
            if (!emptyOrSame)
                continue;

            var max = ModelSlotMax(slot);
            if (max.HasValue && slot._count >= max.Value)
                break;

            return i;
        }

        return -1;
    }

    private static void AddToModelSlot(AmmoSlot slot, IdentifiableType ident, int count, string? id)
    {
        if (slot._count > 0 && slot._id != ident)
        {
            SrLogger.LogDebug($"Saved storage '{id}' holds something else in that slot; the item was not added");
            return;
        }

        slot._id = ident;
        slot._count += count;

        var max = ModelSlotMax(slot);
        if (max.HasValue && slot._count > max.Value)
            slot._count = max.Value;
    }

    // A slot that only exists as saved data may have no size set up (it throws or reads 0); then
    // it is not limited.
    private static int? ModelSlotMax(AmmoSlot slot)
    {
        try
        {
            var max = slot.MaxCount;
            return max > 0 ? max : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}