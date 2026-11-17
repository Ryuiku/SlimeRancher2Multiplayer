using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Sanctuary;

/// <summary>
/// A sanctuary room changed: its theme, or which gadget stands in one of its pre-placed spots.
/// </summary>
internal sealed class SanctuaryRoomPacket : IPacket
{
    public string RoomId = string.Empty;

    // Empty when only a pre-placed gadget changed.
    public string ThemeRefId = string.Empty;

    // Pre-placed spot id and the actor id of the gadget standing there.
    public List<(string GadgetId, long ActorId)> PreplacedGadgets = new();

    public PacketType Type => PacketType.SanctuaryRoom;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(RoomId);
        writer.WriteString(ThemeRefId);
        SanctuaryPacketIo.WritePreplaced(writer, PreplacedGadgets);
    }

    public void Deserialise(PacketReader reader)
    {
        RoomId = reader.ReadPooledString() ?? string.Empty;
        ThemeRefId = reader.ReadPooledString() ?? string.Empty;
        PreplacedGadgets = SanctuaryPacketIo.ReadPreplaced(reader);
    }
}

internal static class SanctuaryPacketIo
{
    public static void WritePreplaced(PacketWriter writer, List<(string GadgetId, long ActorId)> entries)
    {
        writer.WritePackedUInt((uint)entries.Count);
        foreach (var (gadgetId, actorId) in entries)
        {
            writer.WriteString(gadgetId);
            writer.WritePackedLong(actorId);
        }
    }

    public static List<(string GadgetId, long ActorId)> ReadPreplaced(PacketReader reader)
    {
        var count = reader.ReadPackedUInt();
        var entries = new List<(string GadgetId, long ActorId)>((int)Math.Min(count, 1024));
        for (var i = 0; i < count; i++)
            entries.Add((reader.ReadPooledString() ?? string.Empty, reader.ReadPackedLong()));
        return entries;
    }
}
