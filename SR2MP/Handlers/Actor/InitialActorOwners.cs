using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Actor;

[PacketHandler((byte)PacketType.InitialActorOwners, HandlerType.Client)]
internal sealed class InitialActorOwnersHandler : BasePacketHandler<InitialActorOwnersPacket>
{
    protected override bool Handle(InitialActorOwnersPacket packet, IPEndPoint? _)
    {
        // The host's complete list. The actors themselves arrive right after this and pick their
        // owner up from here when they are created.
        NetworkActorManager.KnownOwners.Clear();

        foreach (var (actorId, ownerIndex) in packet.Actors)
        {
            if (ownerIndex < 0 || ownerIndex >= packet.Owners.Count)
                continue;

            NetworkActorManager.RememberOwner(actorId, packet.Owners[ownerIndex]);
        }

        return false;
    }
}
