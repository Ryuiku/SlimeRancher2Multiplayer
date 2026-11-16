using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Loading;

// Who is simulating which actor, sent by the host just before the actors themselves when a player
// joins or resyncs. Without it the newcomer believed nobody owned anything and took over every
// actor around them, including whatever other players were carrying.
internal sealed class InitialActorOwnersPacket : IPacket
{
    // Player ids, then one (actor id, index into Owners) pair per owned actor.
    public List<string> Owners = new();
    public List<(long ActorId, int OwnerIndex)> Actors = new();

    public PacketType Type => PacketType.InitialActorOwners;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.ActorCritical;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedInt(Owners.Count);
        foreach (var owner in Owners)
            writer.WriteString(owner);

        writer.WritePackedInt(Actors.Count);
        foreach (var (actorId, ownerIndex) in Actors)
        {
            writer.WritePackedLong(actorId);
            writer.WritePackedInt(ownerIndex);
        }
    }

    public void Deserialise(PacketReader reader)
    {
        var ownerCount = reader.ReadPackedInt();
        Owners = new List<string>(Math.Max(0, ownerCount));
        for (var i = 0; i < ownerCount; i++)
            Owners.Add(reader.ReadString() ?? string.Empty);

        var actorCount = reader.ReadPackedInt();
        Actors = new List<(long, int)>(Math.Max(0, actorCount));
        for (var i = 0; i < actorCount; i++)
            Actors.Add((reader.ReadPackedLong(), reader.ReadPackedInt()));
    }
}
