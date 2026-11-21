using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Slime.Tangle;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Slime.Tangle;

// Another player's tangle bud stalk: a copy that grows and withers with theirs.
[PacketHandler((byte)PacketType.TangleStalk)]
internal sealed class TangleStalkHandler : BasePacketHandler<TangleStalkPacket>
{
    protected override bool Handle(TangleStalkPacket packet, IPEndPoint? _)
    {
        HandlingPacket = true;
        try
        {
            NetworkTangleManager.HandleStalk(packet);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"TangleStalk: {packet.Event} for stalk {packet.StalkId} failed: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}
