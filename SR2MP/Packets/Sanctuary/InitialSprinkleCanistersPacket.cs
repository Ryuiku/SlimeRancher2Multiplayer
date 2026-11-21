using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Sanctuary;

/// <summary>
/// Every sprinkle canister of the host's world, for a joining player.
/// </summary>
internal sealed class InitialSprinkleCanistersPacket : IPacket
{
    public List<SprinkleCanisterState> Canisters = new();

    public PacketType Type => PacketType.InitialSprinkleCanisters;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedUInt((uint)Canisters.Count);
        foreach (var canister in Canisters)
            canister.Write(writer);
    }

    public void Deserialise(PacketReader reader)
    {
        var count = reader.ReadPackedUInt();
        Canisters = new List<SprinkleCanisterState>((int)Math.Min(count, 1024));
        for (var i = 0; i < count; i++)
            Canisters.Add(SprinkleCanisterState.Read(reader));
    }
}
