using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// A client reached the point where a Labyrinth disruption forecast group unlocks. The host runs
/// the game's own unlock, so its save stays the one that decides, and shares the result.
/// </summary>
internal sealed class PrismaGroupUnlockPacket : IPacket
{
    public string GroupName = string.Empty;

    public PacketType Type => PacketType.PrismaGroupUnlock;
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.WorldState;

    public void Serialise(PacketWriter writer) => writer.WriteString(GroupName);

    public void Deserialise(PacketReader reader) => GroupName = reader.ReadPooledString() ?? string.Empty;
}
