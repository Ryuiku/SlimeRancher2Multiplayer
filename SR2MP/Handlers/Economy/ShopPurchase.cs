using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Economy;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Economy;

[PacketHandler((byte)PacketType.ShopPurchase)]
internal sealed class ShopPurchaseHandler : BasePacketHandler<ShopPurchasePacket>
{
    protected override bool Handle(ShopPurchasePacket packet, IPEndPoint? _)
    {
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
            Apply(packet);

        return true;
    }

    private static void Apply(ShopPurchasePacket packet)
    {
        if (NetworkShopManager.ApplyEntry(packet.CategoryKey, packet.Seed, packet.ItemId, packet.UnlockType, packet.Count))
            NetworkShopManager.Refresh();
    }
}
