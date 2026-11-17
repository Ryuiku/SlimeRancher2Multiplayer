using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// The host's whole Labyrinth disruption forecast. Used both on join and for live changes:
/// the forecast is rolled at random, so clients take the host's instead of rolling their own.
/// </summary>
internal sealed class PrismaDisruptionPacket : IPacket
{
    internal sealed class Entry : INetObject
    {
        public double Start;
        public byte Level;

        public void Serialise(PacketWriter writer)
        {
            writer.WriteDouble(Start);
            writer.WriteByte(Level);
        }

        public void Deserialise(PacketReader reader)
        {
            Start = reader.ReadDouble();
            Level = reader.ReadByte();
        }
    }

    internal sealed class Pattern : INetObject
    {
        public double EndTime;
        public List<Entry> Entries = new();

        public void Serialise(PacketWriter writer)
        {
            writer.WriteDouble(EndTime);
            writer.WriteList(Entries, PacketWriterDels.NetObject<Entry>.Writer);
        }

        public void Deserialise(PacketReader reader)
        {
            EndTime = reader.ReadDouble();
            Entries = reader.ReadList(PacketReaderDels.NetObject<Entry>.Reader) ?? new List<Entry>();
        }
    }

    internal sealed class Area : INetObject
    {
        // The area definition's Guid, or its asset name when it has none.
        public string Guid = string.Empty;
        public double LastCompletionTime;
        public bool ForecastUnlocked;
        public List<Pattern> Patterns = new();

        public void Serialise(PacketWriter writer)
        {
            writer.WriteString(Guid);
            writer.WriteDouble(LastCompletionTime);
            writer.WriteBool(ForecastUnlocked);
            writer.WriteList(Patterns, PacketWriterDels.NetObject<Pattern>.Writer);
        }

        public void Deserialise(PacketReader reader)
        {
            Guid = reader.ReadPooledString() ?? string.Empty;
            LastCompletionTime = reader.ReadDouble();
            ForecastUnlocked = reader.ReadBool();
            Patterns = reader.ReadList(PacketReaderDels.NetObject<Pattern>.Reader) ?? new List<Pattern>();
        }
    }

    public double ForecastedTime;
    public List<Area> Areas = new();

    public PacketType Type => PacketType.PrismaDisruption;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteDouble(ForecastedTime);
        writer.WriteList(Areas, PacketWriterDels.NetObject<Area>.Writer);
    }

    public void Deserialise(PacketReader reader)
    {
        ForecastedTime = reader.ReadDouble();
        Areas = reader.ReadList(PacketReaderDels.NetObject<Area>.Reader) ?? new List<Area>();
    }
}
