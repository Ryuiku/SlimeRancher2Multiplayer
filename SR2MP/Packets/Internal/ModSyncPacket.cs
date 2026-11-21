using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Internal;

internal sealed class ModSyncPacket : IPacket
{
    /// <summary>
    /// Reserved key in <see cref="Mods"/> that carries the client's own Ranching Together version,
    /// so the host can turn away a player whose build can't talk to it (for example the original
    /// mod joining a Patched host). Mod ids are string hashes, so this value won't clash in practice.
    /// </summary>
    public const uint SelfVersionKey = uint.MaxValue;

    public string PlayerId;
    public Dictionary<uint, ModData> Mods;

    public PacketType Type => PacketType.ModSyncAck;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Important;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteStringWithoutSize(PlayerId);
        writer.WriteDictionary(Mods, PacketWriterDels.UInt, PacketWriterDels.NetObject<ModData>.Writer);
    }

    public void Deserialise(PacketReader reader)
    {
        PlayerId = reader.ReadPooledStringOfSize(16)!;
        Mods = reader.ReadDictionary(PacketReaderDels.UInt, PacketReaderDels.NetObject<ModData>.Reader)!;
    }
}

internal sealed class ModData : INetObject
{
    public string Name;
    public string Version;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(Name);
        writer.WriteString(Version);
    }

    public void Deserialise(PacketReader reader)
    {
        Name = reader.ReadPooledString()!;
        Version = reader.ReadPooledString()!;
    }
}