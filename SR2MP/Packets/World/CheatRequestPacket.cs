using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// A client pressed a cheat menu button for something only the host runs (weather, tornados), or
/// that every game has to run its own part of (ranch gardens). The host checks its own cheat setting,
/// runs it, and the normal weather, tornado and actor sync shows the result to everyone.
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

        // Ranch gardens: the host runs its part and tells every other game to run theirs.
        GrowGardens = 5,

        // One weather type (pattern) off in the zone; the others keep running.
        ClearWeatherPattern = 6,
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

    // Tornado: the speed the client tested its path with. Above 0, Position is the tested ground point
    // and Rotation the mover's yaw; otherwise the host looks for a spot itself.
    public float Speed = -1f;

    // Weather: the zone's own pattern the button is for (asset name, as in WeatherPacket). Empty means
    // the zone's pattern that has the state.
    public string PatternName = string.Empty;

    public PacketType Type => PacketType.CheatRequest;
    // Ordered: weather buttons send the level to set, so a resent earlier press must not land after a later one.
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteByte((byte)Action);
        writer.WriteString(Zone);
        writer.WriteInt(StateId);
        writer.WriteFloat(Hours);
        writer.WriteVector3(Position);
        writer.WriteQuaternion(Rotation);
        writer.WriteFloat(Speed);
        writer.WriteString(PatternName);
    }

    public void Deserialise(PacketReader reader)
    {
        Action = (CheatAction)reader.ReadByte();
        Zone = reader.ReadPooledString() ?? string.Empty;
        StateId = reader.ReadInt();
        Hours = reader.ReadFloat();
        Position = reader.ReadVector3();
        Rotation = reader.ReadQuaternion();

        // Fields added later are read only when the sender wrote them, so an older build's request
        // does not run off the end of the packet.
        Speed = reader.BytesRemaining >= sizeof(float) ? reader.ReadFloat() : -1f;
        PatternName = reader.BytesRemaining > 0 ? reader.ReadPooledString() ?? string.Empty : string.Empty;
    }
}
