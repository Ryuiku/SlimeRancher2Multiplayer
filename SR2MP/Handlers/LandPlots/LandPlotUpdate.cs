using System.Net;
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

                // Storage is kept per kind, not per plot type, so the old silo, collector and
                // feeder contents would show up again in a new plot of the same type.
                // The entries stay (a storage without slots breaks the plot for a joining player),
                // only their contents go.
                if (model.siloAmmo != null)
                {
                    foreach (var storage in model.siloAmmo)
                    {
                        var slots = storage.value?.Slots;
                        if (slots == null)
                            continue;

                        foreach (var slot in slots)
                        {
                            if (slot == null)
                                continue;

                            slot._count = 0;
                            slot._id = null;
                        }
                    }
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