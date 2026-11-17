using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Sanctuary;

/// <summary>
/// One sprinkle canister as the player producing its sprinkles sees it: production clock, rate,
/// sprinkles not yet turned into items, and the contents of each slot.
/// </summary>
internal sealed class SprinkleCanisterPacket : IPacket
{
    public SprinkleCanisterState State = new();

    public PacketType Type => PacketType.SprinkleCanister;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer) => State.Write(writer);

    public void Deserialise(PacketReader reader) => State = SprinkleCanisterState.Read(reader);
}

internal sealed class SprinkleCanisterState
{
    public string Id = string.Empty;
    public double LastUpdateTime;
    public double CurrentRate;
    public double AccumulatedSprinkles;

    // Persistent id of the item type (-1 when the slot is empty) and how many are in the slot.
    public List<(int Ident, int Count)> Slots = new();

    public void Write(PacketWriter writer)
    {
        writer.WriteString(Id);
        writer.WriteDouble(LastUpdateTime);
        writer.WriteDouble(CurrentRate);
        writer.WriteDouble(AccumulatedSprinkles);

        writer.WritePackedUInt((uint)Slots.Count);
        foreach (var (ident, count) in Slots)
        {
            writer.WritePackedInt(ident);
            writer.WritePackedInt(count);
        }
    }

    public static SprinkleCanisterState Read(PacketReader reader)
    {
        var state = new SprinkleCanisterState
        {
            Id = reader.ReadPooledString() ?? string.Empty,
            LastUpdateTime = reader.ReadDouble(),
            CurrentRate = reader.ReadDouble(),
            AccumulatedSprinkles = reader.ReadDouble()
        };

        var count = reader.ReadPackedUInt();
        state.Slots = new List<(int Ident, int Count)>((int)Math.Min(count, 64));
        for (var i = 0; i < count; i++)
            state.Slots.Add((reader.ReadPackedInt(), reader.ReadPackedInt()));

        return state;
    }
}
