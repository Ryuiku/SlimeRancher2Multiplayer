using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// A client pressed a cheat menu button for something only the host runs (weather, tornados).
/// The host checks its own cheat setting, runs it, and the normal weather and tornado sync
/// shows the result to everyone.
/// </summary>
internal sealed class CheatRequestPacket : IPacket
{
    internal enum CheatAction : byte
    {
        SetWeather = 1,
        ClearWeather = 2,
        Tornado = 3,

        // A weather on top of what the zone already runs.
        AddWeather = 4,
    }

    public CheatAction Action;

    // Weather: the zone (ZoneDefinition asset name, as in WeatherPacket), the state (save
    // persistence id, as in WeatherPacket) and how long it lasts in game hours.
    public string Zone = string.Empty;
    public int StateId;
    public float Hours;

    // Tornado: where.
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;

    public PacketType Type => PacketType.CheatRequest;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteByte((byte)Action);
        writer.WriteString(Zone);
        writer.WriteInt(StateId);
        writer.WriteFloat(Hours);
        writer.WriteVector3(Position);
        writer.WriteQuaternion(Rotation);
    }

    public void Deserialise(PacketReader reader)
    {
        Action = (CheatAction)reader.ReadByte();
        Zone = reader.ReadPooledString() ?? string.Empty;
        StateId = reader.ReadInt();
        Hours = reader.ReadFloat();
        Position = reader.ReadVector3();
        Rotation = reader.ReadQuaternion();
    }
}
