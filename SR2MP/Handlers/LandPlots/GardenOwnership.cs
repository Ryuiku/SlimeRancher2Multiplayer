using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.LandPlots;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.LandPlots;

[PacketHandler((byte)PacketType.GardenOwnership)]
internal sealed class GardenOwnershipHandler : BasePacketHandler<GardenOwnershipPacket>
{
    protected override bool Handle(GardenOwnershipPacket packet, IPEndPoint? clientEp)
    {
        if (!IsServerSide)
        {
            // From the host: its decision, applied as is.
            foreach (var entry in packet.Entries)
                NetworkGardenManager.ApplyOwnership(entry.GardenId, entry.OwnerId);

            return true;
        }

        string? senderId = null;
        if (clientEp != null && Main.Server.ClientManager.TryGetClient(clientEp, out var client) && client != null)
            senderId = client.PlayerId;

        var verdicts = NetworkGardenManager.Arbitrate(packet.Entries, senderId);
        if (verdicts.Count > 0)
            Main.Server.SendToAll(new GardenOwnershipPacket { Entries = verdicts });

        return false;
    }
}
