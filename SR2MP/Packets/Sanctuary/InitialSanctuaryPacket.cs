using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Sanctuary;

/// <summary>
/// The host's sanctuary rooms for a joining player: each room's theme and the gadgets standing in
/// its pre-placed spots. Owned themes and the sprinkle balance travel with the unlocks and
/// currencies.
/// </summary>
internal sealed class InitialSanctuaryPacket : IPacket
{
    public sealed class Room
    {
        public string RoomId = string.Empty;
        public string ThemeRefId = string.Empty;
        public List<(string GadgetId, long ActorId)> PreplacedGadgets = new();
    }

    public List<Room> Rooms = new();

    public PacketType Type => PacketType.InitialSanctuary;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedUInt((uint)Rooms.Count);
        foreach (var room in Rooms)
        {
            writer.WriteString(room.RoomId);
            writer.WriteString(room.ThemeRefId);
            SanctuaryPacketIo.WritePreplaced(writer, room.PreplacedGadgets);
        }
    }

    public void Deserialise(PacketReader reader)
    {
        var count = reader.ReadPackedUInt();
        Rooms = new List<Room>((int)Math.Min(count, 1024));
        for (var i = 0; i < count; i++)
        {
            Rooms.Add(new Room
            {
                RoomId = reader.ReadPooledString() ?? string.Empty,
                ThemeRefId = reader.ReadPooledString() ?? string.Empty,
                PreplacedGadgets = SanctuaryPacketIo.ReadPreplaced(reader)
            });
        }
    }
}
