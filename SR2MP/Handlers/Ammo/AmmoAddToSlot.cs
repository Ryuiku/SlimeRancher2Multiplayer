using System.Net;
using Il2CppMonomiPark.SlimeRancher.Player;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Ammo;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Ammo;

[PacketHandler((byte)PacketType.AmmoAddToSlot)]
internal sealed class AmmoAddToSlotHandler : BasePacketHandler<AmmoAddToSlotPacket>
{
    protected override bool Handle(AmmoAddToSlotPacket packet, IPEndPoint? _)
    {
        // A sprinkle canister's producer sends its full state soon after any change to it.
        NetworkSprinkleCanisterManager.MarkDirtyByAmmoKey(packet.ID);

        // Storage objects are torn down and rebuilt during a zone load: applied after it.
        // Passed on to the other players right away either way.
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
            Apply(packet);

        return true;
    }

    private static void Apply(AmmoAddToSlotPacket packet)
    {
        var ammo = NetworkAmmoManager.GetAmmo(packet.ID);

        // Not loaded on this machine. The host still passes it on: the other players may have it.
        if (ammo == null) return;

        if (!ActorManager.ActorTypes.TryGetValue(packet.Identifiable, out var ident) || ident == null)
        {
            SrLogger.LogWarning($"AmmoAddToSlot: unknown identifiable type {packet.Identifiable}");
            return;
        }

        if (packet.SlotIndex < 0 || ammo.Slots == null || packet.SlotIndex >= ammo.Slots.Count)
        {
            SrLogger.LogWarning($"AmmoAddToSlot: slot {packet.SlotIndex} out of range for '{packet.ID}' ({ammo.Slots?.Count ?? 0} slots)");
            return;
        }

        HandlingPacket = true;
        ammo.MaybeAddToSpecificSlot(new AmmoSlot.AmmoMetadata(ident), packet.SlotIndex, packet.Count, false);
        HandlingPacket = false;
    }
}