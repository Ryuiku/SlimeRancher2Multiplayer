using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Player;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Player;

// Only stores the choice. Each avatar picks it up itself, so it does not matter whether this
// arrives before or after the player's join or the avatar's spawn.
[PacketHandler((byte)PacketType.PlayerCosmetics)]
internal sealed class PlayerCosmeticsHandler : BasePacketHandler<PlayerCosmeticsPacket>
{
    protected override bool Handle(PlayerCosmeticsPacket packet, IPEndPoint? clientEp)
    {
        if (IsServerSide)
        {
            // A client can only set its own look.
            if (clientEp != null && Main.Server.ClientManager.TryGetClient(clientEp, out var client) && client != null)
                packet.PlayerId = client.PlayerId;

            if (string.IsNullOrEmpty(packet.PlayerId))
                return false;

            PlayerCosmeticsManager.Set(packet.PlayerId, packet.VacPalette);
            return true;
        }

        if (string.IsNullOrEmpty(packet.PlayerId) || packet.PlayerId == LocalID)
            return false;

        PlayerCosmeticsManager.Set(packet.PlayerId, packet.VacPalette);
        return false;
    }
}
