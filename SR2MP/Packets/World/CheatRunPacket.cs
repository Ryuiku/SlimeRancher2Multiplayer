using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// Host -> clients: a cheat menu button was pressed (on the host or a client), and each game runs
/// the part only it can do (the gardens it runs, the produce it runs).
/// </summary>
internal sealed class CheatRunPacket : IPacket
{
    internal enum CheatAction : byte
    {
        GrowGardens = 1,
    }

    public CheatAction Action;

    public PacketType Type => PacketType.CheatRun;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer) => writer.WriteByte((byte)Action);

    public void Deserialise(PacketReader reader) => Action = (CheatAction)reader.ReadByte();
}
