using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Slime.Tabby;

// A stalking slime's (tabby) current stalk step and stealth, sent by its owner when they change.
internal sealed class SlimeStalkPacket : IPacket
{
    public ActorId ActorId;
    // StalkConsumable.Mode (NONE, APPROACH, WAIT, TARGET, PREP, POUNCE).
    public byte Mode;
    // The actor being stalked, 0 if there is none or it is not a synced actor.
    public long TargetActorId;
    public bool Stealth;

    public PacketType Type => PacketType.SlimeStalk;
    // The steps can follow each other within a second (PREP, POUNCE, NONE), so they must arrive in order.
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.ActorCritical;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedLong(ActorId.Value);
        writer.WriteByte(Mode);
        writer.WritePackedLong(TargetActorId);
        writer.WriteBool(Stealth);
    }

    public void Deserialise(PacketReader reader)
    {
        ActorId = new ActorId(reader.ReadPackedLong());
        Mode = reader.ReadByte();
        TargetActorId = reader.ReadPackedLong();
        Stealth = reader.ReadBool();
    }
}
