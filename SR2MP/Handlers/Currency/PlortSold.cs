using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Economy;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Currency;

[PacketHandler((byte)PacketType.PlortSold, HandlerType.Server)]
internal sealed class PlortSoldHandler : BasePacketHandler<PlortSoldPacket>
{
    protected override bool Handle(PlortSoldPacket packet, IPEndPoint? _)
    {
        if (!ActorManager.ActorTypes.TryGetValue(packet.Ident, out var ident) || ident == null)
        {
            SrLogger.LogWarning($"PlortSold: unknown identifiable type {packet.Ident}");
            return false;
        }

        var economy = SceneContext.Instance ? SceneContext.Instance.PlortEconomyDirector : null;
        if (!economy)
            return false;

        // The game's own bookkeeping, so the saturation is exactly what selling here would give.
        // Money is not touched there: it is shared on its own.
        HandlingPacket = true;
        try
        {
            economy!.RegisterSold(ident, packet.Count, packet.Price);
        }
        finally
        {
            HandlingPacket = false;
        }

        MarketPriceSync.MarkDirty();

        // Only the host's count matters, so nobody else needs it.
        return false;
    }
}
