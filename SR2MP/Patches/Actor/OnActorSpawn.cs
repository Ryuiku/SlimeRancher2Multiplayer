using System.Collections;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.VFX;
using SR2MP.Components.Actor;
using SR2MP.Packets.Actor;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Actor;

[HarmonyPatch(typeof(InstantiationHelpers), nameof(InstantiationHelpers.InstantiateActor))]
internal static class OnActorSpawn
{
    private static IEnumerator SpawnOverNetwork(
        int actorType,
        byte sceneGroup,
        GameObject actor,
        SlimeAppearance.AppearanceSaveSet appearance,
        SlimeAppearance.AppearanceSaveSet secondAppearance)
    {
        yield return null;

        if (!actor)
            yield break;

        // A joining client that hasn't received the host's world yet would announce actors with ids
        // that belong to other actors on the host.
        if (!NetworkActorManager.CanSendActorChanges)
            yield break;

        var id = actor.GetComponent<IdentifiableActor>().GetActorId();

        var packet = new ActorSpawnPacket
        {
            ActorType = actorType,
            SceneGroup = sceneGroup,
            ActorId = id,
            Position = actor.transform.position,
            Rotation = actor.transform.rotation,
            SpawnType = (byte)ActorSpawnType.Actor,
            MaterialIndex = (byte)SprinkleMaterialType.none,
            OwnerId = LocalID
        };

        var slimeModel = actor.GetComponent<IdentifiableActor>()._model.TryCast<SlimeModel>();
        if (slimeModel != null)
        {
            packet.SpawnType = (byte)ActorSpawnType.Slime;
            packet.Emotions = slimeModel.Emotions;
            packet.Sleeping = slimeModel.isSleeping;
            
            var firstSet = appearance != SlimeAppearance.AppearanceSaveSet.NONE
                ? appearance
                : slimeModel.firstAppearanceSaveSet;
            var secondSet = secondAppearance != SlimeAppearance.AppearanceSaveSet.NONE
                ? secondAppearance
                : slimeModel.secondAppearanceSaveSet;

            var radiancy = ActorAppearanceType.Default;
            var slimeAppearanceApplicator = actor.GetComponent<SlimeAppearanceApplicator>();
            var slimeDefinition = actor.GetComponent<Identifiable>().identType.TryCast<SlimeDefinition>();
            if (slimeAppearanceApplicator != null)
            {
                var currentAppearance = slimeAppearanceApplicator.Appearance;
                if (slimeDefinition != null && currentAppearance != null)
                {
                    if (currentAppearance == slimeDefinition.RadiantBase)
                        radiancy = ActorAppearanceType.BaseRadiant;
                    else if (currentAppearance == slimeDefinition.RadiantLargo0)
                        radiancy = ActorAppearanceType.LargoRadiant0;
                    else if (currentAppearance == slimeDefinition.RadiantLargo1)
                        radiancy = ActorAppearanceType.LargoRadiant1;
                    else if (firstSet == SlimeAppearance.AppearanceSaveSet.NONE)
                        firstSet = currentAppearance.SaveSet;
                }
            }

            packet.FirstAppearance = firstSet;
            packet.SecondAppearance = secondSet;
            packet.Radiancy = (byte)radiancy;
        }
        else
        {
            var sprinkle = actor.GetComponent<RandomMaterial>();
            if (sprinkle != null)
            {
                packet.SpawnType = (byte)ActorSpawnType.Sprinkle;

                var materialName = sprinkle._renderers
                    .Select(r => r.sharedMaterial?.name.Replace(" (Instance)", ""))
                    .FirstOrDefault();

                if (Enum.TryParse(materialName, out SprinkleMaterialType type))
                    packet.MaterialIndex = (byte)type;
            }
        }

        Main.SendToAllOrServer(packet);
    }

    public static void Postfix(
        GameObject __result,
        GameObject original,
        SceneGroup sceneGroup,
        Vector3 position,
        Quaternion rotation,
        bool nonActorOk = false,
        SlimeAppearance.AppearanceSaveSet appearance = SlimeAppearance.AppearanceSaveSet.NONE,
        SlimeAppearance.AppearanceSaveSet secondAppearance = SlimeAppearance.AppearanceSaveSet.NONE,
        Il2CppSystem.Nullable<AmmoSlot.AmmoMetadata> metadata = null!,
        bool ignoreEmotions = false,
        bool setCollected = false)
    {
        if (HandlingPacket) return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        // The game also calls this for objects that are not actors (nonActorOk), and it can return
        // nothing. An exception here would make the game receive no object at all, in the middle of
        // whatever it was doing, so this must never throw.
        try
        {
            if (!__result || !original)
                return;

            var identifiableActor = __result.GetComponent<IdentifiableActor>();
            if (!identifiableActor || identifiableActor._model == null)
                return;

            var identifiable = original.GetComponent<Identifiable>();
            if (!identifiable || identifiable.identType == null)
                return;

            var model = identifiableActor._model;

            if (NetworkDroneManager.IsDroneModel(model)) return;

            var networkActor = __result.GetComponent<NetworkActor>();
            if (!networkActor)
                networkActor = __result.AddComponent<NetworkActor>();
            networkActor.LocallyOwned = true;
            networkActor.CurrentOwnerId = LocalID;

            var actorType = NetworkActorManager.GetPersistentID(identifiable.identType);
            var sceneGroupId = NetworkSceneManager.GetPersistentID(sceneGroup);

            ActorManager.Actors[model.actorId.Value] = model;

            StartCoroutine(SpawnOverNetwork(actorType, (byte)sceneGroupId, __result, appearance, secondAppearance));
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not register a newly created actor for multiplayer: {ex.Message}");
        }
    }
}