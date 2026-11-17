using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Loading;

/// <summary>
/// The growth of every garden and resource spawner in the host's save, by position. A joining
/// player writes it to the spawners it does not have loaded, so a garden neither player is near
/// grows on the host's timeline instead of the joiner's own save's.
/// </summary>
internal sealed class InitialResourceSpawnersPacket : IPacket
{
    internal struct Entry : INetObject
    {
        public Vector3 Position;
        public double NextSpawnTime;
        public float StoredWater;
        public bool NextSpawnRipens;

        public readonly void Serialise(PacketWriter writer)
        {
            writer.WriteVector3(Position);
            writer.WriteDouble(NextSpawnTime);
            writer.WriteFloat(StoredWater);
            writer.WriteBool(NextSpawnRipens);
        }

        public void Deserialise(PacketReader reader)
        {
            Position = reader.ReadVector3();
            NextSpawnTime = reader.ReadDouble();
            StoredWater = reader.ReadFloat();
            NextSpawnRipens = reader.ReadBool();
        }
    }

    public List<Entry> Entries;

    public PacketType Type => PacketType.InitialResourceSpawners;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Landplots;

    public void Serialise(PacketWriter writer)
        => writer.WriteList(Entries, PacketWriterDels.NetObject<Entry>.Writer);

    public void Deserialise(PacketReader reader)
        => Entries = reader.ReadList(PacketReaderDels.NetObject<Entry>.Reader)!;
}
