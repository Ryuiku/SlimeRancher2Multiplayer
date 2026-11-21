using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// One unlock that counts for every player: a vac palette, a sanctuary theme, a slime
/// appearance or a secret style. One packet for all four, told apart by Kind.
/// </summary>
internal sealed class UnlockSharedPacket : IPacket
{
    public enum UnlockKind : byte
    {
        Palette = 0,
        Theme = 1,
        Appearance = 2,
        SecretStyle = 3
    }

    public UnlockKind Kind;

    // Palettes and themes, by their reference id.
    public string RefId = string.Empty;

    // Appearances and secret styles: the slime's persistent id, and the appearance's save set.
    public int IdentId = -1;
    public byte SaveSet;

    // False takes it away again (only themes have a way to do that).
    public bool Owned = true;

    public PacketType Type => PacketType.UnlockShared;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteByte((byte)Kind);
        writer.WriteString(RefId);
        writer.WritePackedInt(IdentId);
        writer.WriteByte(SaveSet);
        writer.WriteBool(Owned);
    }

    public void Deserialise(PacketReader reader)
    {
        Kind = (UnlockKind)reader.ReadByte();
        RefId = reader.ReadPooledString() ?? string.Empty;
        IdentId = reader.ReadPackedInt();
        SaveSet = reader.ReadByte();
        Owned = reader.ReadBool();
    }
}
