using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Weather;

[PacketHandler((byte)PacketType.TornadoSpawn, HandlerType.Client)]
internal sealed class TornadoSpawnHandler : BasePacketHandler<TornadoSpawnPacket>
{
    protected override bool Handle(TornadoSpawnPacket packet, IPEndPoint? _)
    {
        if (packet.Despawn)
        {
            NetworkTornadoManager.PendingTornados.Remove(packet.TornadoId);

            if (NetworkTornadoManager.ClientTornados.TryGetValue(packet.TornadoId, out var existing))
            {
                NetworkTornadoManager.ClientTornados.Remove(packet.TornadoId);
                
                try
                {
                    // Fade it out the way the game ends a tornado, as the host's copy does.
                    if (existing)
                        NetworkTornadoManager.FadeOut(existing);
                }
                catch (Exception ex)
                {
                    SrLogger.LogWarning($"TornadoSpawnHandler: failed to despawn tornado {packet.TornadoId}: {ex.Message}");
                }
            }
            return false;
        }
        
        // A copy this client already has (a resync or the host's regular resend) is updated in place.
        if (NetworkTornadoManager.TryUpdateExisting(packet.TornadoId, packet.Position, packet.Rotation, packet.Speed, packet.Spin, packet.FadeInWorldSeconds))
        {
            NetworkTornadoManager.PendingTornados.Remove(packet.TornadoId);
            return false;
        }

        if (NetworkTornadoManager.PlayerInDistance(packet.Position))
        {
            NetworkTornadoManager.PendingTornados.Remove(packet.TornadoId);
            NetworkTornadoManager.SpawnTornado(packet.TornadoId, packet.Position, packet.Rotation, packet.Speed, packet.Spin, packet.FadeInWorldSeconds);
        }
        else
        {
            NetworkTornadoManager.PendingTornados[packet.TornadoId] = new NetworkTornadoManager.PendingTornado
            {
                Position = packet.Position,
                Rotation = packet.Rotation,
                Speed = packet.Speed,
                Spin = packet.Spin,
                FadeInWorldSeconds = packet.FadeInWorldSeconds,
                ReceivedAt = UnityEngine.Time.unscaledTime
            };
        }

        return false;
    }
}
