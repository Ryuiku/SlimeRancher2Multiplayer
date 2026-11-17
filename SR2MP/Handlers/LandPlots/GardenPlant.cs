using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.GardenPlant)]
internal sealed class GardenPlantHandler : BasePacketHandler<GardenPlantPacket>
{
    protected override bool Handle(GardenPlantPacket packet, IPEndPoint? _)
    {
        // A garden this save never loaded gets a model, so the crop is there when it loads.
        var model = NetworkLandPlotManager.GetOrCreateModel(packet.ID);
        if (model == null)
            return true;

        if (packet.ActorType == 9)
        {
            model.resourceGrowerDefinition = null;

            if (!model.gameObj) return true;
            var plot = model.gameObj.GetComponentInChildren<LandPlot>();

            HandlingPacket = true;
            plot.DestroyAttached();
            HandlingPacket = false;
        }
        else
        {
            var actor = ActorManager.ActorTypes[packet.ActorType];

            model.resourceGrowerDefinition =
                GameContext.Instance.AutoSaveDirector._saveReferenceTranslation._resourceGrowerTranslation.RawLookupDictionary._entries.FirstOrDefault(x =>
                    x.value._primaryResourceType == actor)!.value;

            if (!model.gameObj) return true;
            var garden = model.gameObj.GetComponentInChildren<GardenCatcher>();

            HandlingPacket = true;
            if (garden.CanAccept(actor))
                garden.Plant(actor, true);
            HandlingPacket = false;
        }

        return true;
    }
}