using System.Net;
using Il2CppMonomiPark.SlimeRancher.Player;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Ammo;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Ammo;

[PacketHandler((byte)PacketType.AmmoAdd)]
internal sealed class AmmoAddHandler : BasePacketHandler<AmmoAddPacket>
{
    protected override bool Handle(AmmoAddPacket packet, IPEndPoint? _)
    {
        // A sprinkle canister's producer sends its full state soon after any change to it.
        NetworkSprinkleCanisterManager.MarkDirtyByAmmoKey(packet.ID);

        // Storage objects are torn down and rebuilt during a zone load: applied after it.
        // Passed on to the other players right away either way.
        if (!NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
            Apply(packet);

        return true;
    }

    private static void Apply(AmmoAddPacket packet)
    {
        var ammo = NetworkAmmoManager.GetAmmo(packet.ID);

        if (!ActorManager.ActorTypes.TryGetValue(packet.Identifiable, out var ident) || ident == null)
        {
            SrLogger.LogWarning($"AmmoAdd: unknown identifiable type {packet.Identifiable}");
            return;
        }

        // Not loaded on this machine: its saved contents are changed instead, so this save (and,
        // on the host, the next join) has it. The host still passes it on either way.
        if (ammo == null)
        {
            NetworkAmmoManager.TryApplyAddToModel(packet.ID, ident, packet.Count);
            return;
        }
        
        var slotIndex = ammo.GetNextSlot(ident);
        if (slotIndex == -1)
            return;

        HandlingPacket = true;
        ammo.MaybeAddToSpecificSlot(new AmmoSlot.AmmoMetadata(ident), slotIndex, packet.Count, false);
        HandlingPacket = false;
    }
}