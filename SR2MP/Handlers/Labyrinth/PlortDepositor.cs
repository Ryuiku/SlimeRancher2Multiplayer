using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.World;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Labyrinth;

[PacketHandler((byte)PacketType.PlortDepositor)]
internal sealed class PlortDepositorHandler : BasePacketHandler<PlortDepositorPacket>
{
    protected override bool Handle(PlortDepositorPacket packet, IPEndPoint? _)
    {
        HandlingPacket = true;
        try
        {
            NetworkPlortDepositorManager.ApplyState(packet.ID, packet.AmountDeposited);
        }
        catch (Exception ex)
        {
            // Still pass it on to the other players (the host returns true below).
            SrLogger.LogWarning($"Could not apply plort depositor {packet.ID}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}
