using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Actor;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Actor;

[PacketHandler((byte)PacketType.ActorSpawn)]
internal sealed class ActorSpawnHandler : BasePacketHandler<ActorSpawnPacket>
{
    protected override bool Handle(ActorSpawnPacket packet, IPEndPoint? _)
    {
        // While this game is loading a zone the spawn is applied afterwards. It is still passed on
        // to the other players right away.
        if (NetworkActorManager.DeferDuringSceneLoad(() => Apply(packet)))
        {
            NetworkActorManager.RememberOwner(packet.ActorId.Value, packet.OwnerId);
            return true;
        }

        return Apply(packet);
    }

    private static bool Apply(ActorSpawnPacket packet)
    {
        if (ActorManager.Actors.ContainsKey(packet.ActorId.Value))
        {
            SrLogger.LogDebug($"Actor {packet.ActorId.Value} already exists");
            return false;
        }

        ActorManager.TrySpawnNetworkActor(
            packet.ActorId, packet.Position, packet.Rotation, packet.ActorType, packet.SceneGroup, out var actor,
            packet.FirstAppearance, packet.SecondAppearance, packet.Emotions, packet.Sleeping,
            packet.SpawnType == (byte)ActorSpawnType.Gadget ? packet.ChargeupTime : 0);

        NetworkActorManager.RememberOwner(packet.ActorId.Value, packet.OwnerId);

        if (actor == null)
            return true;
        
        // Gadgets have no NetworkActor component
        if (actor.TryGetNetworkComponent(out var networkComponent))
        {
            networkComponent.CurrentOwnerId = packet.OwnerId;
            networkComponent.LocallyOwned = false;
            networkComponent.CachedLocallyOwned = false;
        }

        switch (packet.SpawnType)
        {
            case (byte)ActorSpawnType.Sprinkle when packet.MaterialIndex != (byte)SprinkleMaterialType.none:
            {
                var gameObj = actor.GetGameObject();
                if (gameObj)
                    StartCoroutine(NetworkActorManager.ApplySprinkleMaterial(gameObj, (SprinkleMaterialType)packet.MaterialIndex));
                break;
            }

            case (byte)ActorSpawnType.Slime when packet.Radiancy != (byte)ActorAppearanceType.Default:
            {
                var slime = actor.TryCast<SlimeModel>();
                if (slime != null)
                    NetworkActorManager.ApplyRadiancy(slime, (ActorAppearanceType)packet.Radiancy);
                break;
            }
        }

        return true;
    }
}