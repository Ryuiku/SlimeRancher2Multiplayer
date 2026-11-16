using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Ammo;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Ammo;

[PacketHandler((byte)PacketType.AmmoDecrement)]
internal sealed class AmmoDecrementHandler : BasePacketHandler<AmmoDecrementPacket>
{
    protected override bool Handle(AmmoDecrementPacket packet, IPEndPoint? _)
    {
        var ammo = NetworkAmmoManager.GetAmmo(packet.ID);

        // Not loaded on this machine. The host still passes it on: the other players may have it.
        if (ammo == null) return true;

        if (packet.SlotIndex < 0 || ammo.Slots == null || packet.SlotIndex >= ammo.Slots.Count)
            return true;

        HandlingPacket = true;
        try
        {
            ammo.Decrement(packet.SlotIndex, packet.Count);
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}