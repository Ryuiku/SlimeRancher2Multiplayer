using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Economy;

/// <summary>
/// The host's whole Caretaker shop: per category the seed that picks its stock and what has been
/// bought from it. Sent when the host restocks and to joining players.
/// </summary>
internal sealed class ShopStatePacket : IPacket
{
    public sealed class Entry
    {
        public string ItemId = string.Empty;
        public int UnlockType;
        public int Count;
    }

    public sealed class Set
    {
        public string Key = string.Empty;
        public int Seed;
        public List<Entry> Entries = new();
    }

    public List<Set> Sets = new();

    public PacketType Type => PacketType.ShopState;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WritePackedUInt((uint)Sets.Count);
        foreach (var set in Sets)
        {
            writer.WriteString(set.Key);
            writer.WriteInt(set.Seed);
            writer.WritePackedUInt((uint)set.Entries.Count);
            foreach (var entry in set.Entries)
            {
                writer.WriteString(entry.ItemId);
                writer.WritePackedInt(entry.UnlockType);
                writer.WritePackedInt(entry.Count);
            }
        }
    }

    public void Deserialise(PacketReader reader)
    {
        var setCount = reader.ReadPackedUInt();
        Sets = new List<Set>((int)Math.Min(setCount, 1024));
        for (var i = 0; i < setCount; i++)
        {
            var set = new Set
            {
                Key = reader.ReadPooledString() ?? string.Empty,
                Seed = reader.ReadInt()
            };

            var entryCount = reader.ReadPackedUInt();
            set.Entries = new List<Entry>((int)Math.Min(entryCount, 1024));
            for (var j = 0; j < entryCount; j++)
            {
                set.Entries.Add(new Entry
                {
                    ItemId = reader.ReadPooledString() ?? string.Empty,
                    UnlockType = reader.ReadPackedInt(),
                    Count = reader.ReadPackedInt()
                });
            }

            Sets.Add(set);
        }
    }
}
