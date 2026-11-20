using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

internal sealed class TornadoSpawnPacket : IPacket
{
    public int TornadoId;
    public bool Despawn;

    // Host to one client: "your game runs this tornado for everyone". It spawns it with its spawner on
    // and reports its state back, which the host passes on.
    public bool Authority;

    // Hand-overs only: the least game hours it should last (the cheat's minimum).
    public float MinLifetimeHours;
    public Vector3 Position;
    public Quaternion Rotation;

    // SlimenadoMoveSpiral._speed on the host (-1 = unknown).
    public float Speed = -1f;

    // SlimenadoMoveSpiral._rot on the host (-1 = not started yet, so the client keeps the game's own start value).
    public float Spin = -1f;

    // World seconds the host's copy has left before it fades (0 = unknown).
    public double FadeInWorldSeconds;

    public PacketType Type => PacketType.TornadoSpawn;
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.Weather;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedInt(TornadoId);
        writer.WriteBool(Despawn);
        if (!Despawn)
        {
            writer.WriteBool(Authority);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            writer.WriteFloat(Speed);
            writer.WriteFloat(Spin);
            writer.WriteDouble(FadeInWorldSeconds);
            if (Authority)
                writer.WriteFloat(MinLifetimeHours);
        }
    }

    public void Deserialise(PacketReader reader)
    {
        TornadoId = reader.ReadPackedInt();
        Despawn = reader.ReadBool();
        if (!Despawn)
        {
            Authority = reader.ReadBool();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            Speed = reader.ReadFloat();
            Spin = reader.ReadFloat();
            FadeInWorldSeconds = reader.ReadDouble();
            if (Authority)
                MinLifetimeHours = reader.ReadFloat();
        }
    }
}
