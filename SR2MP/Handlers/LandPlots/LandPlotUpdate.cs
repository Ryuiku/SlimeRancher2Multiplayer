using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.LandPlots;

internal abstract class LandPlotUpdateHandler<T> : BasePacketHandler<T> where T : LandPlotUpdatePacket, new()
{
}

[PacketHandler((byte)PacketType.LandPlotUpgrade)]
internal sealed class LandPlotUpgradeHandler : LandPlotUpdateHandler<LandPlotUpgradePacket>
{
    protected override bool Handle(LandPlotUpgradePacket packet, IPEndPoint? _)
    {
        // A plot this save never loaded gets a model, so the upgrade is there when it loads.
        var model = NetworkLandPlotManager.GetOrCreateModel(packet.PlotID);
        if (model == null)
            return true;

        model.upgrades.Add(packet.ID);

        // Not loaded here: the game's deluxe upgrader replants the crop only on a loaded plot, so
        // the saved crop is switched to the deluxe one here, or the plot loads with the normal crop.
        if (packet.ID == LandPlot.Upgrade.DELUXE_GARDEN && !model.gameObj && model.resourceGrowerDefinition != null)
        {
            var crop = NetworkGardenPlanting.CurrentCrop(model);
            var deluxe = crop != null ? NetworkGardenPlanting.DefinitionFor(model, crop) : null;
            if (deluxe != null)
                model.resourceGrowerDefinition = deluxe;
        }

        if (model.gameObj)
        {
            var landPlotComponent = model.gameObj.GetComponentInChildren<LandPlot>();
            HandlingPacket = true;
            landPlotComponent.AddUpgrade(packet.ID);
            HandlingPacket = false;
            
            foreach (var storage in model.gameObj.GetComponentsInChildren<SiloStorage>(true))
            {
                if (storage.Ammo == null) continue;
                model.siloAmmo[storage.AmmoSetReference.Guid] = storage.Ammo._ammoModel;
            }
        }

        return true;
    }
}

[PacketHandler((byte)PacketType.NewLandPlot)]
internal sealed class NewLandPlotHandler : BasePacketHandler<NewLandPlotPacket>
{
    protected override bool Handle(NewLandPlotPacket packet, IPEndPoint? _)
    {
        // A plot this save never loaded gets a model, so the new plot is there when it loads.
        var model = NetworkLandPlotManager.GetOrCreateModel(packet.PlotID);
        if (model == null)
            return true;

        model.typeId = packet.ID;

        if (!model.gameObj)
        {
            // Not loaded here: the game is not around to reset the plot, so the old plot's
            // upgrades and crop would stay on the new one.
            try
            {
                model.upgrades?.Clear();
                model.resourceGrowerDefinition = null;

                // The game gives a replaced plot a fresh model, so its storages start empty and
                // are sized by the new plot when it loads. Keeping the old plot's storages (only
                // emptied) kept their slot count too: a storage with more saved slots than the new
                // plot's made the area fail to load and sent the player to the main menu. Missing
                // storages are fine: the game makes them when the plot loads, and joiners skip them.
                model.siloAmmo = new CppCollections.Dictionary<string, AmmoModel>();

                if (model.siloStorageIndices != null)
                {
                    for (var i = 0; i < model.siloStorageIndices.Length; i++)
                        model.siloStorageIndices[i] = 0;
                }

                model.ashUnits = 0f;
                model.feederCycleSpeed = default;
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Could not reset replaced plot {packet.PlotID}: {ex.Message}");
            }
        }

        if (model.gameObj)
        {
            var location = model.gameObj.GetComponent<LandPlotLocation>();
            var landPlotComponent = model.gameObj.GetComponentInChildren<LandPlot>();

            HandlingPacket = true;
            location.Replace(landPlotComponent,
                GameContext.Instance.LookupDirector._plotPrefabDict[packet.ID]);
            HandlingPacket = false;
        }

        return true;
    }
}