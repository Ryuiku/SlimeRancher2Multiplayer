using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Slime.Sloomber;

internal sealed class SloomberYawnPacket : IPacket
{
    public ActorId ActorId;
    public bool Active;
    // Sent with Active = false: the owner stopped the yawn during its wind-up, before yawning.
    public bool Cancel;
    
    public PacketType Type => PacketType.SloomberYawn;
    // A wind-up must never arrive after the yawn or cancel that ends it.
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.ActorCritical;

    public void Serialise(PacketWriter writer)
    { 
        writer.WritePackedLong(ActorId.Value);
        writer.WriteBool(Active);
        writer.WriteBool(Cancel);
    }

    public void Deserialise(PacketReader reader)
    {
        ActorId = new ActorId(reader.ReadPackedLong());
        Active = reader.ReadBool();
        Cancel = reader.ReadBool();
    }
}