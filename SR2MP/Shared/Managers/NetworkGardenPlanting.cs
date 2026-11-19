using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Components.LandPlots;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Puts another player's crop into a garden plot the way the game's own planting does, without
/// the parts that went wrong when it was done through GardenCatcher.Plant:
/// - Plant only plants into an empty plot, so a plot that still had another crop (two players
///   planting at once, a clear and a plant crossing) silently kept its old crop.
/// - Plant throws in SpawnResource.PlantCrops while the new crop has no saved data yet, after
///   making the crop but before attaching it: the plot stayed empty with a loose crop in it, and
///   the host did not pass the plant on to the other players.
/// - A plot that is not loaded got a grower definition picked by crop type alone. Every crop has
///   several (normal garden, deluxe garden, wild patches), so a deluxe garden came back with the
///   normal crop when its area loaded.
/// </summary>
internal static class NetworkGardenPlanting
{
    internal const int ClearCrop = 9;

    private static GardenCatcher? templateCatcher;

    internal static bool IsDeluxe(LandPlotModel model)
    {
        try
        {
            return model.HasUpgrade(LandPlot.Upgrade.DELUXE_GARDEN);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The crop prefab the game plants for this crop in this catcher's garden.</summary>
    private static GameObject? PrefabFor(GardenCatcher? catcher, IdentifiableType crop, bool deluxe)
    {
        if (!catcher || crop == null)
            return null;

        // Built in Awake. A catcher that never woke (an inactive plot, or the plot prefab) has
        // only its plant list.
        var dict = deluxe ? catcher!._deluxeDict : catcher!._plantableDict;
        if (dict != null && dict.ContainsKey(crop))
        {
            var fromDict = dict[crop];
            if (fromDict)
                return fromDict;
        }

        var slots = catcher.Plantable;
        if (slots == null)
            return null;

        foreach (var slot in slots)
        {
            if (slot == null || slot.IdentType == null || slot.IdentType.Pointer != crop.Pointer)
                continue;

            var prefab = deluxe && slot.DeluxePlantedPrefab ? slot.DeluxePlantedPrefab : slot.PlantedPrefab;
            if (prefab)
                return prefab;
        }

        return null;
    }

    private static GardenCatcher? TemplateCatcher()
    {
        if (templateCatcher)
            return templateCatcher;

        try
        {
            var prefabs = GameContext.Instance ? GameContext.Instance.LookupDirector?._plotPrefabDict : null;
            if (prefabs == null || !prefabs.ContainsKey(LandPlot.Id.GARDEN))
                return null;

            var plotPrefab = prefabs[LandPlot.Id.GARDEN];
            templateCatcher = plotPrefab ? plotPrefab.GetComponentInChildren<GardenCatcher>(true) : null;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Garden: could not read the garden plot prefab: {ex.Message}");
        }

        return templateCatcher;
    }

    private static ResourceGrowerDefinition? DefinitionOf(GameObject? crop)
    {
        if (!crop)
            return null;

        var spawner = crop!.GetComponent<SpawnResource>();
        return spawner ? spawner._resourceGrowerDefinition : null;
    }

    /// <summary>
    /// The grower definition the game itself saves for this crop on a garden plot with these
    /// upgrades. Null when the crop cannot be planted in a garden.
    /// </summary>
    internal static ResourceGrowerDefinition? DefinitionFor(LandPlotModel model, IdentifiableType crop)
        => DefinitionOf(PrefabFor(TemplateCatcher(), crop, IsDeluxe(model)));

    /// <summary>
    /// Makes this game's plot hold this crop: loaded plots get the crop object, unloaded ones get
    /// the saved definition the game builds the crop from when the area loads.
    /// </summary>
    /// <returns>The new crop object when one was made here.</returns>
    internal static GameObject? Plant(LandPlotModel model, IdentifiableType crop, string plotId)
    {
        var deluxe = IsDeluxe(model);
        var plot = model.gameObj ? model.gameObj.GetComponentInChildren<LandPlot>(true) : null;

        if (!plot)
        {
            // A plant that crossed a demolish or a plot change: only gardens hold crops.
            if (model.typeId != LandPlot.Id.GARDEN)
            {
                SrLogger.LogWarning($"Garden {plotId}: the plot is {model.typeId} now, plant ignored");
                return null;
            }

            var definition = DefinitionFor(model, crop);
            if (definition == null)
            {
                SrLogger.LogWarning($"Garden {plotId}: {crop.name} cannot be planted in a garden, ignored");
                return null;
            }

            model.resourceGrowerDefinition = definition;
            SrLogger.LogMessage($"Garden {plotId}: not loaded here, saved {definition.name}");
            return null;
        }

        var catcher = plot!.GetComponentInChildren<GardenCatcher>(true);
        if (plot.TypeId != LandPlot.Id.GARDEN || !catcher)
        {
            SrLogger.LogWarning($"Garden {plotId}: the plot is {plot.TypeId} now, plant ignored");
            return null;
        }

        var prefab = PrefabFor(catcher, crop, deluxe) ?? PrefabFor(TemplateCatcher(), crop, deluxe);
        if (!prefab)
        {
            SrLogger.LogWarning($"Garden {plotId}: no {(deluxe ? "deluxe " : "")}crop for {crop.name} on this plot, ignored");
            return null;
        }

        var wanted = DefinitionOf(prefab);
        var attached = plot._attached;
        if (attached)
        {
            var current = DefinitionOf(attached);
            if (current != null && wanted != null && current.Pointer == wanted.Pointer)
            {
                SrLogger.LogGarden($"Garden {plotId}: already has {wanted.name}");
                return null;
            }

            SrLogger.LogMessage($"Garden {plotId}: replacing {(current != null ? current.name : attached!.name)} with {crop.name}");
            Clear(model, plotId);
        }

        // As GardenCatcher.Plant does it, minus PlantCrops while the crop has no saved data yet:
        // the game sets the times from that data once it has it, and the owner sends its growth.
        var cropObject = Object.Instantiate(prefab, plot.transform);
        var spawner = cropObject.GetComponent<SpawnResource>();
        if (spawner && spawner._model != null)
        {
            try
            {
                spawner.PlantCrops();
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Garden {plotId}: PlantCrops failed: {ex.Message}");
            }
        }

        SECTR_AudioCue? cue = null;
        try
        {
            if (catcher && catcher!.FruitTypeGroup && catcher.FruitTypeGroup.IsMember(crop))
                cue = catcher.TreeScaleUpCue;
        }
        catch
        {
            // the sound is only a nicety
        }

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            plot.Attach(cropObject, false, true, cue);
        }
        finally
        {
            HandlingPacket = wasHandling;
        }

        // Attach saves the crop's own definition; written again in case it threw before that.
        if (wanted != null)
            model.resourceGrowerDefinition = wanted;

        SrLogger.LogMessage($"Garden {plotId}: planted {(wanted != null ? wanted.name : crop.name)}");
        return cropObject;
    }

    internal static void Clear(LandPlotModel model, string plotId)
    {
        model.resourceGrowerDefinition = null;

        var plot = model.gameObj ? model.gameObj.GetComponentInChildren<LandPlot>(true) : null;
        if (!plot || !plot!._attached)
            return;

        var wasHandling = HandlingPacket;
        HandlingPacket = true;
        try
        {
            plot.DestroyAttached();
        }
        finally
        {
            HandlingPacket = wasHandling;
        }

        SrLogger.LogMessage($"Garden {plotId}: cleared");
    }

    /// <summary>The crop type planted in this plot here, or null.</summary>
    internal static IdentifiableType? CurrentCrop(LandPlotModel model)
    {
        try
        {
            var definition = model.resourceGrowerDefinition;
            return definition != null ? definition._primaryResourceType : null;
        }
        catch
        {
            return null;
        }
    }
}
