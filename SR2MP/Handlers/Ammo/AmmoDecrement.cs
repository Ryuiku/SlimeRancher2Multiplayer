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
        // A sprinkle canister's producer sends its full state soon after any change to it.
        NetworkSprinkleCanisterManager.MarkDirtyByAmmoKey(packet.ID);

        // Storage objects are torn down and rebuilt during a zone load: applied after it.
        // Passed on to the other players right away either way.
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
            Apply(packet);

        return true;
    }

    private static void Apply(AmmoDecrementPacket packet)
    {
        var ammo = NetworkAmmoManager.GetAmmo(packet.ID);

        // Not loaded on this machine: its saved contents are changed instead, so this save (and,
        // on the host, the next join) has it. The host still passes it on either way.
        if (ammo == null)
        {
            NetworkAmmoManager.TryApplyDecrementToModel(packet.ID, packet.SlotIndex, packet.Count);
            return;
        }

        if (packet.SlotIndex < 0 || ammo.Slots == null || packet.SlotIndex >= ammo.Slots.Count)
            return;

        HandlingPacket = true;
        try
        {
            ammo.Decrement(packet.SlotIndex, packet.Count);
        }
        finally
        {
            HandlingPacket = false;
        }
    }
}