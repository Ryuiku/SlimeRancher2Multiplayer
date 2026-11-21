using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

internal sealed class MapUnlockPacket : IPacket
{
    public string NodeID;

    // Which record this is: empty for a revealed fog node, otherwise the event key (zone unlocks).
    public string EventKey = string.Empty;

    public PacketType Type => PacketType.MapUnlock;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(NodeID);
        writer.WriteString(EventKey);
    }

    public void Deserialise(PacketReader reader)
    {
        NodeID = reader.ReadPooledString()!;
        EventKey = reader.ReadString() ?? string.Empty;
    }
}