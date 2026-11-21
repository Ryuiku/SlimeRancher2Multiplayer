using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Loading;

/// <summary>
/// What the host's save has unlocked that the game keeps per player: vac palettes, sanctuary
/// themes, slime appearances and secret styles (added to the joiner's own), and the rewards
/// kiosk progress (the host's replaces the joiner's).
/// </summary>
internal sealed class InitialUnlocksPacket : IPacket
{
    public List<string> PaletteRefIds = new();
    public List<string> ThemeRefIds = new();
    public List<(int SlimeId, byte SaveSet)> Appearances = new();
    public List<int> SecretStyles = new();
    public List<(string RewardId, int IdentId, int Count)> RewardProgress = new();

    public PacketType Type => PacketType.InitialUnlocks;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteList(PaletteRefIds, PacketWriterDels.String);
        writer.WriteList(ThemeRefIds, PacketWriterDels.String);

        writer.WritePackedUInt((uint)Appearances.Count);
        foreach (var (slimeId, saveSet) in Appearances)
        {
            writer.WritePackedInt(slimeId);
            writer.WriteByte(saveSet);
        }

        writer.WriteList(SecretStyles, PacketWriterDels.PackedInt);

        writer.WritePackedUInt((uint)RewardProgress.Count);
        foreach (var (rewardId, identId, count) in RewardProgress)
        {
            writer.WriteString(rewardId);
            writer.WritePackedInt(identId);
            writer.WritePackedInt(count);
        }
    }

    public void Deserialise(PacketReader reader)
    {
        PaletteRefIds = reader.ReadList(PacketReaderDels.String)!.ConvertAll(id => id ?? string.Empty);
        ThemeRefIds = reader.ReadList(PacketReaderDels.String)!.ConvertAll(id => id ?? string.Empty);

        var appearanceCount = reader.ReadPackedUInt();
        Appearances = new List<(int SlimeId, byte SaveSet)>((int)appearanceCount);
        for (var i = 0; i < appearanceCount; i++)
            Appearances.Add((reader.ReadPackedInt(), reader.ReadByte()));

        SecretStyles = reader.ReadList(PacketReaderDels.PackedInt)!;

        var progressCount = reader.ReadPackedUInt();
        RewardProgress = new List<(string RewardId, int IdentId, int Count)>((int)progressCount);
        for (var i = 0; i < progressCount; i++)
            RewardProgress.Add((reader.ReadPooledString() ?? string.Empty, reader.ReadPackedInt(), reader.ReadPackedInt()));
    }
}
