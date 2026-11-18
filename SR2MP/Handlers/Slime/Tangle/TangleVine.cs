using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Slime.Tangle;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Slime.Tangle;

// Another player's tangle firing its grab vine: a copy of the vine that plays the same steps.
[PacketHandler((byte)PacketType.TangleVine)]
internal sealed class TangleVineHandler : BasePacketHandler<TangleVinePacket>
{
    protected override bool Handle(TangleVinePacket packet, IPEndPoint? _)
    {
        HandlingPacket = true;
        try
        {
            NetworkTangleManager.HandleVine(packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"TangleVine: step {packet.Phase} for tangle {packet.ActorId.Value} failed: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}
