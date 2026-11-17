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
        ClearReward = 2
    }

    public string RewardId = string.Empty;
    public int IdentId = -1;
    public int Count;
    public ProgressOp Op;

    public PacketType Type => PacketType.EndGameRewardProgress;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(RewardId);
        writer.WritePackedInt(IdentId);
        writer.WritePackedInt(Count);
        writer.WriteByte((byte)Op);
    }

    public void Deserialise(PacketReader reader)
    {
        RewardId = reader.ReadPooledString() ?? string.Empty;
        IdentId = reader.ReadPackedInt();
        Count = reader.ReadPackedInt();
        Op = (ProgressOp)reader.ReadByte();
    }
}
