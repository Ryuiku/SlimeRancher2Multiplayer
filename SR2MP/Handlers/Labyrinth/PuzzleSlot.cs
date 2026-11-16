using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.World;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Labyrinth;

[PacketHandler((byte)PacketType.PuzzleSlot)]
internal sealed class PuzzleSlotHandler : BasePacketHandler<PuzzleSlotPacket>
{
    protected override bool Handle(PuzzleSlotPacket packet, IPEndPoint? _)
    {
        HandlingPacket = true;
        try
        {
            NetworkPuzzleSlotManager.ApplyState(packet.ID, packet.Filled);
        }
        catch (Exception ex)
        {
            // Still passed on to the other players.
            SrLogger.LogWarning($"Could not apply puzzle slot {packet.ID}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }
        
        return true;
    }
}
