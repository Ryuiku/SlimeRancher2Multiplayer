using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.GordoSlime;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.GordoSlime;

[PacketHandler((byte)PacketType.GordoSlimeFeed)]
internal sealed class GordoSlimeFeedHandler : BasePacketHandler<GordoSlimeFeedPacket>
{
    protected override bool Handle(GordoSlimeFeedPacket packet, IPEndPoint? _)
    {
        // The sender fed the last piece; the rewards are theirs to give out.
        if (packet.RequiredFoodCount > 0 && packet.NewFoodCount >= packet.RequiredFoodCount)
            NetworkGordoSlimeManager.MarkRemoteFinisher(packet.ID);

        if (GameState.gordos.TryGetValue(packet.ID, out var gordoSlime))
        {
            gordoSlime.GordoEatenCount = packet.NewFoodCount;
        }
        else
        {
            gordoSlime = new GordoModel
            {
                fashions = new CppCollections.List<IdentifiableType>(0),
                gordoEatCount = packet.NewFoodCount,
                gordoSeen = false,
                gameObj = null,
                targetCount = packet.RequiredFoodCount,
                identifiableType = ActorManager.ActorTypes[packet.GordoSlimeType]
            };

            GameState.gordos.Add(packet.ID, gordoSlime);
        }

        return true;
    }
}