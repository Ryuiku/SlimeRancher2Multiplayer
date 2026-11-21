using Il2CppMonomiPark.SlimeRancher.DataModel;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Land plot models for plot locations this save has never loaded. The game only makes a plot's
/// model when its location first loads, so a player whose save never visited a ranch expansion
/// had nowhere to keep the host's plot there, and the plot came back as in their own save.
/// </summary>
internal static class NetworkLandPlotManager
{
    /// <summary>
    /// The plot's model, made the way the game makes one for a new location when there is none.
    /// When the location loads later, the game finds this model and builds the plot from it.
    /// </summary>
    internal static LandPlotModel? GetOrCreateModel(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var plots = SceneContext.Instance ? SceneContext.Instance.GameModel?.landPlots : null;
        if (plots == null)
            return null;

        if (plots.TryGetValue(id, out var model) && model != null)
            return model;

        try
        {
            model = SceneContext.Instance.GameModel.InitializeLandPlotModel(id);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"The game could not make a model for plot {id}: {ex.Message}");
            model = null;
        }

        try
        {
            if (model == null)
            {
                model = new LandPlotModel();
                model.Init();
            }

            // Whether the game's own call keeps the model is not known, so it is kept here too.
            if (!plots.ContainsKey(id))
                plots[id] = model;

            model.siloAmmo ??= new CppCollections.Dictionary<string, AmmoModel>();
            model.upgrades ??= new CppCollections.HashSet<LandPlot.Upgrade>();
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not make a model for plot {id}: {ex.Message}");
            return plots.TryGetValue(id, out var existing) ? existing : null;
        }

        SrLogger.LogMessage($"Created plot model {id} for a plot another player has");
        return model;
    }
}
