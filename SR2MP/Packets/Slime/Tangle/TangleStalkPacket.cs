using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Slime.Tangle;

// A tangle bud stalk, sent by the game that grew it (the tangle's owner, or the host for tangle
// weather). The bud itself is a normal actor; this only lets the others see the stalk it hangs on.
internal sealed class TangleStalkPacket : IPacket
{
    public enum StalkEvent : byte
    {
        // Sent when the stalk appears and again every few seconds while it stands, so players who
        // join or walk into the area later see it too, and a copy whose grower went away expires.
        Spawn = 0,
        // The stalk withers (its bud was picked, broke or expired).
        Out = 1,
        // The stalk is gone without withering.
        Remove = 2
    }

    public StalkEvent Event;
    public long StalkId;

    // Out only: wither without the pluck sound.
    public bool Silent;

    // Spawn only.
    public string PrefabName = string.Empty;
    public string SceneGroup = string.Empty;
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;
    public bool ShouldSpawnSeed;
    public float SeedDuration;
    public bool IsBigSeed;
    // The bud hanging on the stalk (0 before it has grown). Others leave it with the grower until
    // someone picks it.
    public long BudActorId;

    public PacketType Type => PacketType.TangleStalk;
    // Spawn must arrive before Out or Remove.
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.ActorCritical;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteByte((byte)Event);
        writer.WritePackedLong(StalkId);

        switch (Event)
        {
            case StalkEvent.Spawn:
                writer.WriteString(PrefabName);
                writer.WriteString(SceneGroup);
                writer.WriteVector3(Position);
                writer.WriteQuaternion(Rotation);
                writer.WriteBool(ShouldSpawnSeed);
                writer.WriteFloat(SeedDuration);
                writer.WriteBool(IsBigSeed);
                writer.WritePackedLong(BudActorId);
                break;
            case StalkEvent.Out:
                writer.WriteBool(Silent);
                break;
        }
    }

    public void Deserialise(PacketReader reader)
    {
        Event = (StalkEvent)reader.ReadByte();
        StalkId = reader.ReadPackedLong();

        switch (Event)
        {
            case StalkEvent.Spawn:
                PrefabName = reader.ReadString() ?? string.Empty;
                SceneGroup = reader.ReadString() ?? string.Empty;
                Position = reader.ReadVector3();
                Rotation = reader.ReadQuaternion();
                ShouldSpawnSeed = reader.ReadBool();
                SeedDuration = reader.ReadFloat();
                IsBigSeed = reader.ReadBool();
                BudActorId = reader.ReadPackedLong();
                break;
            case StalkEvent.Out:
                Silent = reader.ReadBool();
                break;
        }
    }
}
