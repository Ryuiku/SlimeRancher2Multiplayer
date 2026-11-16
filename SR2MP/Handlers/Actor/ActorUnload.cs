using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Actor;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Actor;

[PacketHandler((byte)PacketType.ActorUnload)]
internal sealed class ActorUnloadHandler : BasePacketHandler<ActorUnloadPacket>
{
    protected override bool Handle(ActorUnloadPacket packet, IPEndPoint? _)
    {
        // If this game can't take the actor over (its zone isn't loaded here), the host still passes
        // the message on: another player may be standing right next to it.
        if (!ActorManager.Actors.TryGetValue(packet.ActorId.Value, out var actor))
            return true;

        if (!actor.TryGetNetworkComponent(out var component))
            return true;

        if (!component.RegionMember)
            return true;
        
        if (!string.IsNullOrEmpty(component.CurrentOwnerId) &&
            component.CurrentOwnerId != packet.SenderId)
            return false;

        // A joining player whose world isn't loaded yet must not take anything over.
        if (!component.RegionMember!._hibernating && SR2MP.Shared.Managers.NetworkActorManager.CanSendActorChanges)
        {
            component.LocallyOwned = true;
            component.CurrentOwnerId = LocalID;
            component.LastLocalClaimTime = UnityEngine.Time.time;
            SR2MP.Shared.Managers.NetworkActorManager.RememberOwner(packet.ActorId.Value, LocalID);

            Main.SendToAllOrServer(new ActorTransferPacket
            {
                ActorId = packet.ActorId,
                OwnerId = LocalID
            });
            return false;
        }
        
        component.LocallyOwned = false;
        return true;
    }
}