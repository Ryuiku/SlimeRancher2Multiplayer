using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Components.LandPlots;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.LandPlots;

// todo: review
// not sure about the whole coroutine and inactive stuff

[PacketHandler((byte)PacketType.InitialLandPlots, HandlerType.Client)]
internal sealed class InitialLandPlotsHandler : BasePacketHandler<InitialLandPlotsPacket>
{
    protected override bool Handle(InitialLandPlotsPacket packet, IPEndPoint? _)
    {
        LogPlotsOnlyInThisSave(packet);

        foreach (var plot in packet.LandPlots)
        {
            // One plot failing must not stop every plot after it from loading.
            try
            {
            // A plot location this save never loaded gets a model here, so the host's plot is
            // there when the area loads instead of this save's (usually empty) one.
            var model = NetworkLandPlotManager.GetOrCreateModel(plot.ID);
            if (model == null)
            {
                SrLogger.LogWarning($"Could not load land plot {plot.ID}: no model");
                continue;
            }

            if (model.gameObj)
            {
                HandlingPacket = true;
                var location = model.gameObj.GetComponent<LandPlotLocation>();
                var landPlotComponent = model.gameObj.GetComponentInChildren<LandPlot>(true);
                location.Replace(landPlotComponent, GameContext.Instance.LookupDirector._plotPrefabDict[plot.Type]);

                var landPlotComponent2 = model.gameObj.GetComponentInChildren<LandPlot>(true);
                landPlotComponent2.ApplyUpgrades(plot.Upgrades.Cast<CppCollections.IEnumerable<LandPlot.Upgrade>>(), false);
                HandlingPacket = false;
            }

            model.typeId = plot.Type;
            model.upgrades = plot.Upgrades;

            switch (plot.Data)
            {
                case InitialLandPlotsPacket.GardenData { Crop: 9 }:
                {
                    model.resourceGrowerDefinition = null;
                    if (!model.gameObj)
                        continue;
                    var gardenPlot = model.gameObj.GetComponentInChildren<LandPlot>(true);
                    HandlingPacket = true;
                    gardenPlot.DestroyAttached();
                    HandlingPacket = false;
                    break;
                }

                case InitialLandPlotsPacket.GardenData garden:
                {
                    var actor = ActorManager.ActorTypes[garden.Crop];
                    model.resourceGrowerDefinition =
                        GameContext.Instance.AutoSaveDirector._saveReferenceTranslation._resourceGrowerTranslation
                            .RawLookupDictionary._entries.FirstOrDefault(x =>
                                x.value._primaryResourceType == actor)!.value;

                    if (!model.gameObj)
                        continue;

                    var gardenCatcher = model.gameObj.GetComponentInChildren<GardenCatcher>();

                    HandlingPacket = true;
                    if (gardenCatcher.CanAccept(actor))
                    {
                        var plantedObject = gardenCatcher.Plant(actor, true);

                        if (plantedObject != null)
                        {
                            var spawnResource = plantedObject.GetComponent<SpawnResource>();
                            if (spawnResource != null && spawnResource._model != null)
                            {
                                foreach (var joint in spawnResource.SpawnJoints)
                                {
                                    if (joint == null || joint.connectedBody == null)
                                        continue;

                                    var connectedObj = joint.connectedBody.gameObject;
                                    Destroyer.Destroy(connectedObj, "InitialLandPlotsHandler.OnDestroy");
                                }
                            }
                        }
                    }
                    HandlingPacket = false;
                    
                    // No time means the host has this garden unloaded and sent nothing: zeros would
                    // make the crop ripe at once. Its saved growth comes with the resource spawners.
                    var existingNetworkGarden = model.gameObj.GetComponentInChildren<NetworkGarden>(true);
                    if (existingNetworkGarden != null && garden.NextSpawnTime > 0)
                        existingNetworkGarden.ApplyUpdate(garden.NextSpawnTime, garden.StoredWater, garden.NextSpawnRipens);

                    break;
                }

                case InitialLandPlotsPacket.SiloData silo:
                {
                    if (!HasSlots(silo.Ammo))
                        break;

                    var ammo = silo.Ammo.ToGameAmmo();
                    model.siloAmmo[SiloAmmo] = ammo._ammoModel;
                    model.siloStorageIndices = Array.ConvertAll(silo.SelectedSlots.ToArray(), input => (int)input);

                    if (!model.gameObj) break;

                    var storage = model.gameObj.GetComponentInChildren<SiloStorage>();
                    storage.Ammo = ammo;
                    storage.SetModel(model);
                    storage.Ammo.ApplySlotData(silo.Ammo);

                    foreach (var activator in model.gameObj.GetComponentsInChildren<SiloStorageActivator>())
                        activator.OnActiveSlotChanged();

                    break;
                }

                case InitialLandPlotsPacket.CoopPondData pond:
                {
                    // The host reads a coop's collector from the coop's own ammo set, so it is kept
                    // under the same key here (it used to land under the feeder's key, and the real
                    // coop storage kept this save's contents).
                    var ammoKey = plot.Type == LandPlot.Id.COOP
                        ? CoopAmmo
                        : PlortCollectorAmmo;
                    if (!HasSlots(pond.CollectorAmmo))
                        break;

                    var collectorAmmo = pond.CollectorAmmo!.ToGameAmmo();
                    model.siloAmmo ??= new CppCollections.Dictionary<string, AmmoModel>();
                    model.siloAmmo[ammoKey] = collectorAmmo._ammoModel;

                    if (!model.gameObj) break;

                    SiloStorage? pondStorage = null;
                    var storages = model.gameObj.GetComponentsInChildren<SiloStorage>(true);
                    foreach (var candidate in storages)
                    {
                        if (candidate && candidate.AmmoSetReference?.Guid == ammoKey)
                        {
                            pondStorage = candidate;
                            break;
                        }
                    }

                    if (!pondStorage && storages.Length > 0)
                    {
                        // Tells from the log if this plot uses another ammo set than expected.
                        var found = string.Join(", ", storages.Select(s => s && s.AmmoSetReference ? s.AmmoSetReference.Guid : "none"));
                        SrLogger.LogWarning($"Plot {plot.ID} ({plot.Type}) has no storage for ammo set {ammoKey} (found: {found}); using the first one");
                        pondStorage = storages[0];
                    }

                    if (pondStorage)
                    {
                        pondStorage!.Ammo = collectorAmmo;
                        pondStorage.SetModel(model);
                        pondStorage.Ammo.ApplySlotData(pond.CollectorAmmo);
                    }
                    break;
                }

                case InitialLandPlotsPacket.CorralData corral:
                {
                    // A corral without a plort collector or feeder upgrade has no storage for it on
                    // the host. A storage with no slots made the game throw and the plot failed.
                    var plortCollectorAmmo = HasSlots(corral.PlortCollectorAmmo) ? corral.PlortCollectorAmmo.ToGameAmmo() : null;
                    if (plortCollectorAmmo != null)
                        model.siloAmmo[PlortCollectorAmmo] = plortCollectorAmmo._ammoModel;

                    var feederAmmo = HasSlots(corral.AutoFeederAmmo) ? corral.AutoFeederAmmo.ToGameAmmo() : null;
                    if (feederAmmo != null)
                        model.siloAmmo[FeederAmmo] = feederAmmo._ammoModel;

                    model.feederCycleSpeed = (SlimeFeeder.FeedSpeed)corral.AutoFeederSpeed;

                    // The host's feeding schedule and collector time, so this corral feeds and
                    // collects in step with the host's instead of on this save's clock.
                    try
                    {
                        model.nextFeedingTime = corral.NextFeedingTime;
                        model.remainingFeedOperations = corral.RemainingFeedOperations;
                        model.collectorNextTime = corral.CollectorNextTime;
                    }
                    catch (Exception ex)
                    {
                        SrLogger.LogDebug($"Could not set the feeder schedule of {plot.ID}: {ex.Message}");
                    }

                    if (!model.gameObj) break;

                    foreach (var corralStorage in model.gameObj.GetComponentsInChildren<SiloStorage>(true))
                    {
                        switch (corralStorage.AmmoSetReference.Guid)
                        {
                            case PlortCollectorAmmo when plortCollectorAmmo != null:
                                corralStorage.Ammo = plortCollectorAmmo;
                                corralStorage.SetModel(model);
                                corralStorage.Ammo.ApplySlotData(corral.PlortCollectorAmmo);
                                break;

                            case FeederAmmo when feederAmmo != null:
                                corralStorage.Ammo = feederAmmo;
                                corralStorage.SetModel(model);
                                corralStorage.Ammo.ApplySlotData(corral.AutoFeederAmmo);
                                break;
                        }
                    }

                    var feeder = model.gameObj.GetComponentInChildren<FeederUpgrader>(true)
                        .Feeder
                        .transform
                        .GetChild(0)
                        .GetComponent<SlimeFeeder>();

                    if (!feeder)
                    {
                        SrLogger.LogError($"Feeder for {plot.ID} is null, tried to set it to {(SlimeFeeder.FeedSpeed)corral.AutoFeederSpeed}");
                        break;
                    }

                    HandlingPacket = true;
                    feeder?.SetFeederSpeed((SlimeFeeder.FeedSpeed)corral.AutoFeederSpeed);
                    feeder?.SetFeederSpeedIcon((SlimeFeeder.FeedSpeed)corral.AutoFeederSpeed);
                    HandlingPacket = false;

                    break;
                }

                case InitialLandPlotsPacket.IncineratorData incinerator:
                {
                    var incineratorAmmo = HasSlots(incinerator.PlortCollectorAmmo) ? incinerator.PlortCollectorAmmo!.ToGameAmmo() : null;
                    if (incineratorAmmo != null)
                        model.siloAmmo[PlortCollectorAmmo] = incineratorAmmo._ammoModel;
                    try { model.ashUnits = incinerator.AshLevel; } catch { /* ignored */ }

                    if (!model.gameObj) break;

                    var incineratorStorage = model.gameObj.GetComponentInChildren<SiloStorage>(true);
                    if (incineratorStorage && incineratorAmmo != null)
                    {
                        incineratorStorage.Ammo = incineratorAmmo;
                        incineratorStorage.SetModel(model);
                        incineratorStorage.Ammo.ApplySlotData(incinerator.PlortCollectorAmmo);
                    }

                    model.gameObj.GetComponentInChildren<FillableAshSource>().SetModel(model);
                    break;
                }
            }
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not load land plot {plot.ID}: {ex.Message}");
            }
            finally
            {
                HandlingPacket = false;
            }
        }

        return false;
    }

    // A plot in this save at a location the host has no plot for. Not removed: whether this save's
    // own plot should go is the players' choice. Only logged so it shows up when testing.
    private static void LogPlotsOnlyInThisSave(InitialLandPlotsPacket packet)
    {
        try
        {
            var hostIds = new HashSet<string>(packet.LandPlots.Select(p => p.ID));
            foreach (var pair in GameState.landPlots)
            {
                if (pair.value == null || pair.value.typeId == LandPlot.Id.EMPTY || hostIds.Contains(pair.key))
                    continue;

                SrLogger.LogWarning($"This save has a {pair.value.typeId} plot at {pair.key} that the host's world does not have; it is kept as it is");
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not compare the plots with the host's: {ex.Message}");
        }
    }

    // The host had nothing to describe for this storage (not built, or never loaded there). The
    // storage the game made for the plot is left as it is.
    private static bool HasSlots(SR2MP.Packets.Ammo.NetworkAmmo? ammo) => ammo?.AmmoSlots != null && ammo.AmmoSlots.Count > 0;
}