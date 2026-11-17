using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// How far each ranch area has been simulated in world time. Every player sends it for the ranch
/// areas awake on their game; the host adds who runs the catch-up of each area. A player whose
/// area wakes up later then only catches up the time nobody simulated.
/// </summary>
internal sealed class RanchCellSyncPacket : IPacket
{
    internal struct Entry : INetObject
    {
        public string CellId;
        public double SimulatedUntil;

        // Only set by the host: the player whose game runs this area's catch-up. Empty: unknown.
        public string SimulatorId;

        public readonly void Serialise(PacketWriter writer)
        {
            writer.WriteString(CellId);
            writer.WriteDouble(SimulatedUntil);
            writer.WriteString(SimulatorId);
        }

        public void Deserialise(PacketReader reader)
        {
            CellId = reader.ReadPooledString()!;
            SimulatedUntil = reader.ReadDouble();
            SimulatorId = reader.ReadPooledString() ?? string.Empty;
        }
    }

    // The host's snapshot for a joining player: the times replace the joiner's own save's.
    public bool Snapshot;
    public List<Entry> Entries;

    public PacketType Type => PacketType.RanchCellSync;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Landplots;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteBool(Snapshot);
        writer.WriteList(Entries, PacketWriterDels.NetObject<Entry>.Writer);
    }

    public void Deserialise(PacketReader reader)
    {
        Snapshot = reader.ReadBool();
        Entries = reader.ReadList(PacketReaderDels.NetObject<Entry>.Reader)!;
    }
}
