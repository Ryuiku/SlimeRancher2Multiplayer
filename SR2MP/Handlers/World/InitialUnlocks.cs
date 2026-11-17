using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Utils;

namespace SR2MP.Handlers.World;

[PacketHandler((byte)PacketType.InitialUnlocks, HandlerType.Client)]
internal sealed class InitialUnlocksHandler : BasePacketHandler<InitialUnlocksPacket>
{
    protected override bool Handle(InitialUnlocksPacket packet, IPEndPoint? _)
    {
        UnlockLookup.ApplyInitialPacket(packet);
        return false;
    }
}
