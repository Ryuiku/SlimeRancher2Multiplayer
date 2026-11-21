using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Slime.Tangle;

// A tangle's grab vine, sent by the tangle's owner as the vine goes through its steps. Only the
// owner's vine holds anything; the others see a copy that plays the same steps.
internal sealed class TangleVinePacket : IPacket
{
    public ActorId ActorId;

    // Which firing of the vine, so a late step from the tangle's previous owner never touches the
    // copy of the new owner's vine.
    public long VineId;

    // GroundVine.Phase the vine went into: 1 grown and holding, 2 pulling in, 3 dropping. 0 means
    // the vine is gone.
    public byte Phase;

    // Phase 1 only: where the vine grew.
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;

    public PacketType Type => PacketType.TangleVine;
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.ActorCritical;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedLong(ActorId.Value);
        writer.WritePackedLong(VineId);
        writer.WriteByte(Phase);

        if (Phase == 1)
        {
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
        }
    }

    public void Deserialise(PacketReader reader)
    {
        ActorId = new ActorId(reader.ReadPackedLong());
        VineId = reader.ReadPackedLong();
        Phase = reader.ReadByte();

        if (Phase == 1)
        {
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
        }
    }
}
