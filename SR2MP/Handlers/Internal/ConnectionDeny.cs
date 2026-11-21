using System.Net;
using SR2MP.Components.UI;
using SR2MP.Packets.Internal;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Internal;

[PacketHandler((byte)PacketType.ConnectionDeny, HandlerType.Client)]
internal sealed class ConnectionDenyHandler : BasePacketHandler<ConnectionDenyPacket>
{
    protected override bool Handle(ConnectionDenyPacket packet, IPEndPoint? _)
    {
        Main.Client.UpdateConnectionStatus(false);

        // Show the host's reason (wrong version, missing mods) instead of a bare "denied".
        var message = string.IsNullOrWhiteSpace(packet.Reason)
            ? "Connection was denied!"
            : $"Connection was denied! {packet.Reason}";
        SrLogger.LogMessage(message);
        MultiplayerUI.Instance.RegisterSystemMessage(message,
            $"SYSTEM_CONNECTION_DENY_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", MultiplayerUI.SystemMessageClose);
        return false;
    }
}