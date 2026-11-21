using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Weather;

// Both ways: the host sends the tornados it runs and hands a tornado to a client's game when the host
// player is not near it; that client then reports the tornado's state, which the host passes on.
[PacketHandler((byte)PacketType.TornadoSpawn)]
internal sealed class TornadoSpawnHandler : BasePacketHandler<TornadoSpawnPacket>
{
    protected override bool Handle(TornadoSpawnPacket packet, IPEndPoint? clientEp)
    {
        // Host: only the game that runs a tornado may report it; true passes it on to the others.
        if (IsServerSide)
            return clientEp != null && NetworkTornadoManager.HandleFromClient(packet, clientEp);

        if (packet.Authority)
        {
            NetworkTornadoManager.SpawnOwned(packet);
            return false;
        }

        // A tornado this game runs: only the host's despawn (it took the tornado back or ended it) applies.
        if (NetworkTornadoManager.OwnedTornados.TryGetValue(packet.TornadoId, out var own))
        {
            if (packet.Despawn)
            {
                NetworkTornadoManager.OwnedTornados.Remove(packet.TornadoId);
                try
                {
                    if (own)
                        NetworkTornadoManager.FadeOut(own);
                }
                catch (Exception ex)
                {
                    SrLogger.LogWarning($"TornadoSpawnHandler: failed to despawn tornado {packet.TornadoId}: {ex.Message}");
                }
            }
            return false;
        }

        // A copy this game already has (a resync or the regular resend) is updated in place, a new one
        // is spawned when this player is near and kept pending otherwise.
        if (packet.Despawn)
            NetworkTornadoManager.ApplyDespawn(packet.TornadoId);
        else
            NetworkTornadoManager.ApplyCopyState(packet);

        return false;
    }
}
