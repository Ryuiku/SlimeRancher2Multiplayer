using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.World;

// Not passed on: the host runs the request (or hands a tornado to the requesting player's game; for
// gardens it also tells the other clients to run their part), and its weather, tornado or actor sync
// shows the result.
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
                result = NetworkCheats.SetWeatherByIds(packet.Zone, packet.StateId, packet.Hours, NetworkCheats.WeatherMode.Set, packet.PatternName);
                break;
            case CheatRequestPacket.CheatAction.AddWeather:
                result = NetworkCheats.SetWeatherByIds(packet.Zone, packet.StateId, packet.Hours, NetworkCheats.WeatherMode.Add, packet.PatternName);
                break;
            case CheatRequestPacket.CheatAction.ClearWeather:
                // Weather ids start at 0 (a real weather), so "none" is -1.
                result = NetworkCheats.SetWeatherByIds(packet.Zone, -1, packet.Hours, NetworkCheats.WeatherMode.Clear);
                break;
            case CheatRequestPacket.CheatAction.ClearWeatherPattern:
                result = NetworkCheats.SetWeatherByIds(packet.Zone, -1, packet.Hours, NetworkCheats.WeatherMode.ClearPattern, packet.PatternName);
                break;
            case CheatRequestPacket.CheatAction.Tornado:
                result = NetworkCheats.SpawnRequestedTornado(packet.Position, packet.Rotation, packet.Speed, clientEp);
                break;
            case CheatRequestPacket.CheatAction.GrowGardens:
                result = NetworkCheats.GrowRanchGardens(out _);

                // The client who asked already ran its own part when it pressed.
                Main.Server.SendToAllExcept(new CheatRunPacket { Action = CheatRunPacket.CheatAction.GrowGardens }, clientEp);
                break;
            default:
                result = "unknown request";
                break;
        }

        SrLogger.LogMessage($"Cheat menu request {packet.Action} from {clientEp}: {result}");
        return false;
    }
}
