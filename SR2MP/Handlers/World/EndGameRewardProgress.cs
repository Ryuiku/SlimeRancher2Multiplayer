using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Utils;

namespace SR2MP.Handlers.World;

[PacketHandler((byte)PacketType.EndGameRewardProgress)]
internal sealed class EndGameRewardProgressHandler : BasePacketHandler<EndGameRewardProgressPacket>
{
    protected override bool Handle(EndGameRewardProgressPacket packet, IPEndPoint? _)
    {
        try
        {
            UnlockLookup.ApplyRewardProgress(packet.Op, packet.RewardId, packet.IdentId, packet.Count);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply rewards kiosk progress {packet.RewardId}: {ex.Message}");
        }

        return true;
    }
}
