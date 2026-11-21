using SR2MP.Packets.Utils;

namespace SR2MP.Packets.LandPlots;

internal sealed class GardenUpdatePacket : IPacket
{
    internal struct Entry : INetObject
    {
        public string GardenId;
        public double NextSpawnTime;
        public float StoredWater;
        public bool NextSpawnRipens;

        // Where the garden's saved data sits: a game that does not have the garden loaded finds
        // its saved data by this position, the way the game itself does.
        public Vector3 Position;

        public readonly void Serialise(PacketWriter writer)
        {
            writer.WriteString(GardenId);
            writer.WriteDouble(NextSpawnTime);
            writer.WriteFloat(StoredWater);
            writer.WriteBool(NextSpawnRipens);
            writer.WriteVector3(Position);
        }

        public void Deserialise(PacketReader reader)
        {
            GardenId = reader.ReadPooledString()!;
            NextSpawnTime = reader.ReadDouble();
            StoredWater = reader.ReadFloat();
            NextSpawnRipens = reader.ReadBool();
            Position = reader.ReadVector3();
        }
    }

    public List<Entry> Entries;

    public PacketType Type => PacketType.GardenUpdate;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Landplots;

    public void Serialise(PacketWriter writer)
        => writer.WriteList(Entries, PacketWriterDels.NetObject<Entry>.Writer);

    public void Deserialise(PacketReader reader)
        => Entries = reader.ReadList(PacketReaderDels.NetObject<Entry>.Reader)!;
}