using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Economy;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Economy;

[PacketHandler((byte)PacketType.ShopState, HandlerType.Client)]
internal sealed class ShopStateHandler : BasePacketHandler<ShopStatePacket>
{
    protected override bool Handle(ShopStatePacket packet, IPEndPoint? _)
    {
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
            Apply(packet);

        return false;
    }

    private static void Apply(ShopStatePacket packet)
    {
        if (NetworkShopManager.ApplySnapshot(packet.Sets))
            NetworkShopManager.Refresh();
    }
}
