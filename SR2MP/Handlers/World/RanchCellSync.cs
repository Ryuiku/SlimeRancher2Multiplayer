using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.World;

[PacketHandler((byte)PacketType.RanchCellSync)]
internal sealed class RanchCellSyncHandler : BasePacketHandler<RanchCellSyncPacket>
{
    protected override bool Handle(RanchCellSyncPacket packet, IPEndPoint? clientEp)
    {
        if (IsServerSide)
        {
            // Only the host sends a snapshot.
            if (packet.Snapshot)
                return false;

            string? senderId = null;
            if (clientEp != null && Main.Server.ClientManager.TryGetClient(clientEp, out var client) && client != null)
                senderId = client.PlayerId;

            // Who simulates an area is the host's to say; a player's report is only its time.
            for (var i = 0; i < packet.Entries.Count; i++)
            {
                var entry = packet.Entries[i];
                entry.SimulatorId = string.Empty;
                packet.Entries[i] = entry;
            }

            NetworkRanchCellManager.Apply(packet, senderId);

            // Passed on so every player knows how far each area has been simulated.
            return true;
        }

        NetworkRanchCellManager.Apply(packet, null);
        return false;
    }
}
