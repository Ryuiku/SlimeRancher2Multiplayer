using System.Net;
using SR2MP.Packets;
using SR2MP.Packets.Internal;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Internal;

[PacketHandler((byte)PacketType.Connect, HandlerType.Server)]
internal sealed class ConnectHandler : BasePacketHandler<ConnectPacket>
{
    protected override bool Handle(ConnectPacket packet, IPEndPoint? clientEp)
    {
        if (clientEp == null)
            return false;

        SrLogger.LogMessage(
            $"Connect request received with PlayerId: {packet.PlayerId}",
            $"Connect request from {clientEp} with PlayerId: {packet.PlayerId}");

        // Connecting again from the same address before the old connection timed out: both sides
        // count from 1 again, and nothing still being resent to the old connection may reach it.
        if (Main.Server.ClientManager.TryGetClient(clientEp, out _))
            Main.Server.ForgetSequences(clientEp);

        Main.Server.ClientManager.AddClient(clientEp, packet.PlayerId);

        var informPacket = new EmptyPacket()
        {
            Type = PacketType.ModSync,
            Reliability = PacketReliability.ReliableOrdered
        };
        Main.Server.SendToClient(informPacket, clientEp);
        return false;
    }
}