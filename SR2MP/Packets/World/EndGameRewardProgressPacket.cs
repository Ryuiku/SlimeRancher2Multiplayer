using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// A change to the rewards kiosk progress, which the game keeps per player. Shared so the
/// kiosk counts everyone's deposits towards the same tracks.
/// </summary>
internal sealed class EndGameRewardProgressPacket : IPacket
{
    public enum ProgressOp : byte
    {
        Set = 0,
        ClearItem = 1,
        ClearReward = 2,

        // A client's deposit completed a track: it asks the host before paying out.
        ClaimPayout = 3,

        // The host's answer to a claim: this player pays the track out.
        GrantPayout = 4
    }

    public string RewardId = string.Empty;
    public int IdentId = -1;
    public int Count;
    public ProgressOp Op;

    // Set only: the count the sender had before its change, so the host can add a deposit made
    // at the same time as another one instead of losing it. -1: the count is final (the host's).
    public int Previous = -1;

    public PacketType Type => PacketType.EndGameRewardProgress;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(RewardId);
        writer.WritePackedInt(IdentId);
        writer.WritePackedInt(Count);
        writer.WriteByte((byte)Op);
        writer.WritePackedInt(Previous);
    }

    public void Deserialise(PacketReader reader)
    {
        RewardId = reader.ReadPooledString() ?? string.Empty;
        IdentId = reader.ReadPackedInt();
        Count = reader.ReadPackedInt();
        Op = (ProgressOp)reader.ReadByte();
        Previous = reader.ReadPackedInt();
    }
}
