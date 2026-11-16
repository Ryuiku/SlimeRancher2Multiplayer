using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Actor;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Actor;

[PacketHandler((byte)PacketType.ActorUpdate)]
internal sealed class ActorUpdateHandler : BasePacketHandler<ActorUpdatePacket>
{
    protected override bool Handle(ActorUpdatePacket packet, IPEndPoint? _)
    {
        // The host passes position updates on even when it can't use them itself. It used to drop
        // them whenever the actor's zone wasn't loaded on the host, so two players exploring together
        // away from the host never saw each other's slimes move, and kept taking them from each other.
        if (!ActorManager.Actors.TryGetValue(packet.ActorId.Value, out var model))
        {
            // Not loaded here. Still remember where it is, so it shows up in the right place when
            // this player reaches its zone.
            try
            {
                if (GameState.TryGetIdentifiableModel(packet.ActorId, out var unloaded)
                    && unloaded.ident != null && !unloaded.ident.IsPlayer)
                {
                    var unloadedActor = unloaded.TryCast<ActorModel>();
                    if (unloadedActor != null)
                    {
                        unloadedActor.lastPosition = packet.Position;
                        unloadedActor.lastRotation = packet.Rotation;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return true;
        }

        var actor = model.TryCast<ActorModel>();
        if (actor == null)
            return true;

        if (actor.ident != null && actor.ident.IsPlayer)
            return false;

        actor.lastPosition = packet.Position;
        actor.lastRotation = packet.Rotation;

        if (!actor.TryGetNetworkComponent(out var networkComponent))
            return true;

        networkComponent.OnNetworkWorldUpdate(packet);

        if (networkComponent.RegionMember?._hibernating == true)
        {
            networkComponent.transform.position = packet.Position;
            networkComponent.transform.rotation = packet.Rotation;
        }

        return true;
    }
}