using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Labyrinth;

[PacketHandler((byte)PacketType.InitialPuzzleSlots, HandlerType.Client)]
internal sealed class InitialPuzzleSlotsHandler : BasePacketHandler<InitialPuzzleSlotsPacket>
{
    protected override bool Handle(InitialPuzzleSlotsPacket packet, IPEndPoint? _)
    {
        var hostIds = new HashSet<string>();
        foreach (var slot in packet.Slots)
            hostIds.Add(slot.ID);

        SR2MP.Shared.Utils.HostWorldState.DropEntriesHostLacks(
            GameState.slots, hostIds, m => m.gameObj, "puzzle slots");

        foreach (var slot in packet.Slots)
        {
            // One slot failing must not stop every slot after it from loading.
            HandlingPacket = true;
            try
            {
                NetworkPuzzleSlotManager.ApplyState(slot.ID, slot.Filled);
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not load puzzle slot {slot.ID}: {ex.Message}");
            }
            finally
            {
                HandlingPacket = false;
            }
        }

        return false;
    }
}
