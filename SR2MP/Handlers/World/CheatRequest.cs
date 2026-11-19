using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.World;

// Not passed on: the host runs the request and its weather or tornado sync shows the result.
[PacketHandler((byte)PacketType.CheatRequest, HandlerType.Server)]
internal sealed class CheatRequestHandler : BasePacketHandler<CheatRequestPacket>
{
    protected override bool Handle(CheatRequestPacket packet, IPEndPoint? clientEp)
    {
        // The host's live setting, not the copy taken when the server started.
        if (!Main.AllowCheats)
        {
            SrLogger.LogWarning($"Ignored a cheat menu request ({packet.Action}) from {clientEp}: cheats are off on this server");
            return false;
        }

        string result;
        switch (packet.Action)
        {
            case CheatRequestPacket.CheatAction.SetWeather:
                result = NetworkCheats.SetWeatherByIds(packet.Zone, packet.StateId, packet.Hours);
                break;
            case CheatRequestPacket.CheatAction.ClearWeather:
                result = NetworkCheats.SetWeatherByIds(packet.Zone, 0, packet.Hours);
                break;
            case CheatRequestPacket.CheatAction.Tornado:
                result = NetworkCheats.SpawnTornadoAt(packet.Position, packet.Rotation);
                break;
            default:
                result = "unknown request";
                break;
        }

        SrLogger.LogMessage($"Cheat menu request {packet.Action} from {clientEp}: {result}");
        return false;
    }
}
