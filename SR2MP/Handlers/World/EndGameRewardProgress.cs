using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Patches.Economy;
using SR2MP.Shared.Utils;

namespace SR2MP.Handlers.World;

[PacketHandler((byte)PacketType.EndGameRewardProgress)]
internal sealed class EndGameRewardProgressHandler : BasePacketHandler<EndGameRewardProgressPacket>
{
    protected override bool Handle(EndGameRewardProgressPacket packet, IPEndPoint? clientEp)
    {
        switch (packet.Op)
        {
            case EndGameRewardProgressPacket.ProgressOp.ClaimPayout:
                if (IsServerSide)
                    OnKioskDispense.OnClaim(packet.RewardId, clientEp);
                return false;

            case EndGameRewardProgressPacket.ProgressOp.GrantPayout:
                if (!IsServerSide)
                    OnKioskDispense.OnGrant(packet.RewardId);
                return false;
        }

        try
        {
            // A deposit that clashed with another one: the host's merged count already went out.
            if (IsServerSide && UnlockLookup.MergeConcurrentRewardProgress(packet))
                return false;

            UnlockLookup.ApplyRewardProgress(packet.Op, packet.RewardId, packet.IdentId, packet.Count);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply rewards kiosk progress {packet.RewardId}: {ex.Message}");
        }

        return true;
    }
}
