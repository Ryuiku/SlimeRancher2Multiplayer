using SR2MP.Packets.Utils;

namespace SR2MP.Packets.Player;

/// <summary>
/// A player's own look, chosen in their own save: for now the vac palette, by its reference id.
/// Only shown on that player's avatar; it is never written to anyone else's save.
/// </summary>
internal sealed class PlayerCosmeticsPacket : IPacket
{
    public string PlayerId = string.Empty;

    // Empty means the game's default vac palette.
    public string VacPalette = string.Empty;

    public PacketType Type => PacketType.PlayerCosmetics;
    public PacketReliability Reliability => PacketReliability.ReliableOrdered;
    public NetworkChannel Channel => NetworkChannel.Important;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteString(PlayerId);
        writer.WriteString(VacPalette);
    }

    public void Deserialise(PacketReader reader)
    {
        PlayerId = reader.ReadPooledString() ?? string.Empty;
        VacPalette = reader.ReadPooledString() ?? string.Empty;
    }
}
