using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Slime.Dervish;
using Il2CppMonomiPark.SlimeRancher.Slime.Slumber;
using Il2CppMonomiPark.SlimeRancher.Slime.Tangle;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Client.Managers;
using SR2MP.Components.Actor;
using SR2MP.Components.LandPlots;
using SR2MP.Packets.World;
using SR2MP.Server.Managers;

namespace SR2MP.Shared.Managers;

/// <summary>
/// What the multiplayer cheat menu buttons do. Each returns a short result for the button label.
/// Everything here goes through the game's own code paths, so the existing sync carries it:
/// time skips through the fast-forward sync, weather through the host's forecast, stalks, buds,
/// yawns, cyclones and gordo bursts through their own packets. Things only the host runs
/// (weather, tornados) are asked of the host by a client.
/// </summary>
internal static class NetworkCheats
{
    internal static bool InSession => Main.Server.IsRunning || Main.Client.IsConnected;

    internal static bool IsClient => Main.Client.IsConnected && !Main.Server.IsRunning;

    /// <summary>Cheats are always fine alone; in multiplayer the host decides.</summary>
    internal static bool Allowed => !InSession || (Main.Server.IsRunning ? Main.AllowCheats : CheatsEnabled);

    // How far around the player the "nearby" buttons look.
    private const float NearbyRadius = 40f;

    // How long cheat weather lasts, in game hours (a game hour is a real minute).
    internal const float WeatherHours = 4f;

    /// <summary>Runs an action a few frames from now. A new closure each time, as Starlight keys
    /// its pending actions by the delegate.</summary>
    internal static void Later(Action action, int frames)
        => ExecuteInTicks(() => action(), frames);

    private static bool OwnedHere(Component component)
    {
        if (!InSession)
            return true;

        var actor = component.GetComponentInParent<NetworkActor>();
        return actor && actor!.LocallyOwned;
    }

    private static Vector3? PlayerPosition()
    {
        var player = SceneContext.Instance ? SceneContext.Instance.Player : null;
        return player ? player!.transform.position : null;
    }

    private static Transform? CameraTransform()
    {
        var camera = SceneContext.Instance ? SceneContext.Instance.Camera : null;
        if (camera)
            return camera!.transform;

        var main = Camera.main;
        return main ? main!.transform : null;
    }

    // ---------------------------------------------------------------- rest

    internal enum RestUntil
    {
        Morning,
        Noon,
        Evening,
        Midnight,
    }

    internal static string RestLabel(RestUntil until) => until switch
    {
        RestUntil.Morning => "Rest until morning (6:00)",
        RestUntil.Noon => "Rest until noon (12:00)",
        RestUntil.Evening => "Rest until evening (18:00)",
        _ => "Rest until midnight (0:00)",
    };

    /// <summary>
    /// What resting in the ranch house does, without the house: the player is held in place while
    /// time runs to the chosen hour, then wakes with the wake sound and the game saves. The skip
    /// reaches the other players through the fast-forward sync, the same as a real sleep.
    /// </summary>
    internal static string Rest(RestUntil until)
    {
        var scene = SceneContext.Instance;
        var time = scene ? scene.TimeDirector : null;
        var playerLock = LockOnDeath.Instance;
        if (!time || !playerLock)
            return "not in a game";

        if (playerLock!.Locked())
            return "already resting";

        if (time!.IsFastForwarding())
            return "time is already skipping";

        var target = until switch
        {
            RestUntil.Morning => time.GetNextDawn(),
            RestUntil.Noon => time.GetNextHour(12f),
            RestUntil.Evening => time.GetNextDusk(),
            _ => time.GetNextHour(0f),
        };

        if (target <= time.WorldTime())
            return "already that time";

        SrLogger.LogMessage($"Cheat menu: resting until {until} ({(target - time.WorldTime()) / 3600:F1} game hours)");
        playerLock.LockUntil(target, 0f, null);
        return "resting";
    }

    // ---------------------------------------------------------------- weather

    internal sealed class WeatherChoice
    {
        public string Label = string.Empty;

        // Null for clear skies.
        public WeatherStateDefinition? State;
    }

    private static string StateLabel(WeatherStateDefinition state)
    {
        var name = state.name ?? string.Empty;
        if (name.EndsWith(" State", StringComparison.Ordinal))
            name = name[..^6];
        return name.Trim();
    }

    /// <summary>The weather zone the player is in: the zone tracker's zone when it has weather,
    /// otherwise the zone whose weather is loaded in the scene.</summary>
    internal static ZoneDefinition? CurrentWeatherZone(out WeatherRegistry.ZoneWeatherData? data)
    {
        data = null;
        var scene = SceneContext.Instance;
        var registry = scene ? scene.WeatherRegistry : null;
        if (!registry || registry!._zones == null)
            return null;

        try
        {
            var tracker = scene!.PlayerZoneTracker;
            var zone = tracker ? tracker!.GetCurrentZone() : null;
            if (zone && registry._zones.TryGetValue(zone, out var found) && found != null)
            {
                data = found;
                return zone;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Cheat menu: zone tracker failed: {ex.Message}");
        }

        foreach (var pair in registry._zones)
        {
            var director = pair.Value?.Director;
            if (pair.Key && director && director!.gameObject.scene.IsValid() && director.gameObject.activeInHierarchy)
            {
                data = pair.Value;
                return pair.Key;
            }
        }

        return null;
    }

    /// <summary>Clear skies, then every weather the zone's own patterns can show.</summary>
    internal static List<WeatherChoice> WeatherChoicesHere(out string zoneName)
    {
        var choices = new List<WeatherChoice> { new() { Label = "Clear skies" } };
        zoneName = string.Empty;

        var zone = CurrentWeatherZone(out var data);
        if (!zone || data == null)
            return choices;

        zoneName = zone!.name;
        var patterns = data.Config ? data.Config.Patterns : null;
        if (patterns == null)
            return choices;

        NetworkWeatherManager.CheckInitialized();
        var seen = new HashSet<IntPtr>();
        var found = new List<WeatherChoice>();
        foreach (var state in NetworkWeatherManager.WeatherStates.Values)
        {
            if (!state || !seen.Add(state.Pointer))
                continue;

            var asState = state.Cast<IWeatherState>();
            for (var i = 0; i < patterns.Count; i++)
            {
                var pattern = patterns[i];
                if (pattern && pattern.ContainsState(asState))
                {
                    found.Add(new WeatherChoice { Label = StateLabel(state), State = state });
                    break;
                }
            }
        }

        found.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        choices.AddRange(found);
        return choices;
    }

    /// <summary>Sets or clears the weather in the player's zone: the host does it, a client asks.</summary>
    internal static string ApplyWeatherHere(WeatherChoice choice)
    {
        var zone = CurrentWeatherZone(out _);
        if (!zone)
            return "no weather here";

        if (IsClient)
        {
            var stateId = choice.State ? NetworkWeatherManager.GetPersistentID(choice.State!) : 0;
            if (choice.State && stateId == 0)
                return "unknown weather";

            Main.Client.SendPacket(new CheatRequestPacket
            {
                Action = choice.State ? CheatRequestPacket.CheatAction.SetWeather : CheatRequestPacket.CheatAction.ClearWeather,
                Zone = zone!.name,
                StateId = stateId,
                Hours = WeatherHours,
            });
            return "asked the host";
        }

        return SetWeather(zone!, choice.State, WeatherHours);
    }

    /// <summary>Host: a client's request, by the names and ids the weather sync uses.</summary>
    internal static string SetWeatherByIds(string zoneName, int stateId, float hours)
    {
        var registry = SceneContext.Instance ? SceneContext.Instance.WeatherRegistry : null;
        if (!registry || registry!._zones == null)
            return "no weather registry";

        ZoneDefinition? zone = null;
        foreach (var pair in registry._zones)
        {
            if (pair.Key && pair.Key.name == zoneName)
            {
                zone = pair.Key;
                break;
            }
        }

        if (!zone)
            return $"no weather zone '{zoneName}'";

        WeatherStateDefinition? state = null;
        if (stateId != 0)
        {
            NetworkWeatherManager.CheckInitialized();
            if (!NetworkWeatherManager.WeatherStates.TryGetValue(stateId, out state) || !state)
                return $"unknown weather {stateId}";
        }

        return SetWeather(zone!, state, Mathf.Clamp(hours, 0.25f, 12f));
    }

    /// <summary>
    /// Host or alone: makes the zone's forecast hold just this weather from now for the given
    /// hours (or nothing, to clear it), the way the game's own forecast does. Running weather is
    /// ended now and upcoming weather in that window dropped; the registry then stops and starts
    /// the states on its next update, which the weather sync passes on.
    /// </summary>
    internal static string SetWeather(ZoneDefinition zone, WeatherStateDefinition? state, float hours)
    {
        var scene = SceneContext.Instance;
        var registry = scene ? scene.WeatherRegistry : null;
        var time = scene ? scene.TimeDirector : null;
        if (!registry || !time || registry!._zones == null)
            return "not in a game";

        if (!registry._zones.TryGetValue(zone, out var data) || data == null)
            return "no weather here";

        WeatherPatternDefinition? pattern = null;
        IWeatherState? asState = null;
        if (state)
        {
            asState = state!.Cast<IWeatherState>();
            var patterns = data.Config ? data.Config.Patterns : null;
            if (patterns != null)
            {
                for (var i = 0; i < patterns.Count; i++)
                {
                    var candidate = patterns[i];
                    if (candidate && candidate.ContainsState(asState))
                    {
                        pattern = candidate;
                        break;
                    }
                }
            }

            if (!pattern)
                return $"{zone.name} has no {StateLabel(state)}";
        }

        var forecast = data.Forecast;
        if (forecast == null)
            return "no forecast";

        var now = time!.WorldTime();
        var end = now + (double)hours * 3600.0;
        var extended = false;

        for (var i = forecast.Count - 1; i >= 0; i--)
        {
            var entry = forecast[i];
            if (entry == null)
            {
                forecast.RemoveAt(i);
                continue;
            }

            if (entry.Started)
            {
                // The same weather already running just goes on longer.
                if (asState != null && pattern && entry.State != null && entry.Pattern
                    && entry.State.Pointer == asState.Pointer && entry.Pattern.Pointer == pattern!.Pointer)
                {
                    entry.EndTime = Math.Max(entry.IsEndTimeKnown ? entry.EndTime : end, end);
                    entry.IsEndTimeKnown = true;
                    extended = true;
                    continue;
                }

                // Ended on the registry's next update, which stops it.
                entry.EndTime = now;
                entry.IsEndTimeKnown = true;
            }
            else if (entry.StartTime < end)
            {
                forecast.RemoveAt(i);
            }
        }

        if (asState != null && !extended)
        {
            forecast.Insert(0, new WeatherModel.ForecastEntry
            {
                Pattern = pattern,
                State = asState,
                StartTime = now,
                EndTime = end,
                IsEndTimeKnown = true,
                Started = false,
            });
        }

        // The registry skips a zone until its next change time.
        data.NextChangeAt = now;

        // The stop and start each send the forecast, but a stop is sent before the registry takes
        // the ended entry out, so send it again once it has.
        if (Main.Server.IsRunning)
        {
            Later(WeatherUpdateHelper.SendWeatherUpdate, 3);
            Later(WeatherUpdateHelper.SendWeatherUpdate, 30);
        }

        var what = state ? $"{StateLabel(state!)} for {hours:0.#} h" : "clear skies";
        SrLogger.LogMessage($"Cheat menu: weather in {zone.name} set to {what}");
        return extended ? "made longer" : "done";
    }

    // ---------------------------------------------------------------- tornado

    internal static string TornadoHere()
    {
        var position = PlayerPosition();
        var view = CameraTransform();
        if (position == null || !view)
            return "not in a game";

        var forward = view!.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;
        forward.Normalize();

        var spot = position.Value + forward * 25f;
        if (Physics.Raycast(spot + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore))
            spot = hit.point;

        var rotation = Quaternion.LookRotation(forward);

        if (IsClient)
        {
            Main.Client.SendPacket(new CheatRequestPacket
            {
                Action = CheatRequestPacket.CheatAction.Tornado,
                Position = spot,
                Rotation = rotation,
            });
            return "asked the host";
        }

        return SpawnTornadoAt(spot, rotation);
    }

    /// <summary>Host or alone: the weather's own tornado spawn, which the tornado sync shares.</summary>
    internal static string SpawnTornadoAt(Vector3 position, Quaternion rotation)
    {
        var activity = NetworkTornadoManager.LastActivity;
        if (activity == null)
        {
            var all = Resources.FindObjectsOfTypeAll<SpawnTornadoActivity>();
            activity = all != null && all.Length > 0 ? all[0] : null;
        }

        if (activity == null)
            return "no tornado found in the game";

        var tornado = activity.Spawn(position, rotation);
        return tornado ? "spawned" : "the game made no tornado";
    }

    // ---------------------------------------------------------------- tangle

    private static TangleSpawnBudMetadata? StalkConfig()
    {
        foreach (var config in Resources.FindObjectsOfTypeAll<TangleSpawnBudMetadata>())
        {
            if (config && config.VineBudWithStalkPrefab)
                return config;
        }

        foreach (var activity in Resources.FindObjectsOfTypeAll<SpawnTangleStalkActivity>())
        {
            var config = activity ? activity.Config : null;
            if (config && config!.VineBudWithStalkPrefab)
                return config;
        }

        return null;
    }

    /// <summary>
    /// Grows a tangle bud stalk on the ground the player looks at, without the seed wait. The
    /// stalk is this game's own, so the tangle sync shows it to the others and the bud is a normal
    /// shared actor.
    /// </summary>
    internal static string GrowTangleStalk()
    {
        var view = CameraTransform();
        var scene = SceneContext.Instance;
        if (!view || !scene)
            return "not in a game";

        var config = StalkConfig();
        if (!config)
            return "no tangle stalk found in the game";

        var mask = (int)config!.SpawnTerrainMask;
        if (mask == 0)
            mask = ~0;

        if (!Physics.Raycast(view!.position, view.forward, out var hit, 60f, mask, QueryTriggerInteraction.Ignore))
            return "look at the ground";

        var stalkObject = InstantiationHelpers.InstantiateDynamic(
            config.VineBudWithStalkPrefab, hit.point, Quaternion.FromToRotation(Vector3.up, hit.normal));
        if (!stalkObject)
            return "the game made no stalk";

        var stalk = stalkObject.GetComponent<VineBulbWithStalk>();
        if (!stalk)
        {
            Object.Destroy(stalkObject);
            return "the game made no stalk";
        }

        var regions = scene!.RegionRegistry;
        stalk.Init(regions ? regions!.CurrentSceneGroup : null, false, 0f, false);
        return "grown";
    }

    // ---------------------------------------------------------------- gardens

    /// <summary>
    /// Ranch gardens and trees this game runs grow new produce now, and the produce already on
    /// them ripens. Produce and growth reach the others through the garden sync. Gardens another
    /// player runs are left to them.
    /// </summary>
    internal static string GrowRanchGardens(out int skipped)
    {
        skipped = 0;
        var scene = SceneContext.Instance;
        var time = scene ? scene.TimeDirector : null;
        if (!time)
            return "not in a game";

        var now = time!.WorldTime();
        var grown = 0;

        foreach (var spawner in Object.FindObjectsOfType<SpawnResource>())
        {
            if (!spawner || !spawner.enabled || !spawner.GetComponentInParent<LandPlot>())
                continue;

            if (InSession)
            {
                var garden = spawner.GetComponent<NetworkGarden>();
                if (garden && !garden!.OwnedHere)
                {
                    skipped++;
                    continue;
                }
            }

            var model = spawner._model;
            if (model == null)
                continue;

            if (model.nextSpawnTime > now)
                model.nextSpawnTime = now;

            var spawned = spawner._spawned;
            if (spawned != null)
            {
                for (var i = 0; i < spawned.Count; i++)
                {
                    var produce = spawned[i];
                    var cycle = produce ? produce.GetComponent<ResourceCycle>() : null;
                    if (!cycle || !OwnedHere(cycle!))
                        continue;

                    try
                    {
                        cycle!.ImmediatelyRipen(0f);
                    }
                    catch (Exception ex)
                    {
                        SrLogger.LogDebug($"Cheat menu: ripen failed: {ex.Message}");
                    }
                }
            }

            grown++;
        }

        SrLogger.LogMessage($"Cheat menu: grew {grown} ranch gardens ({skipped} run by another player)");
        return grown == 0 && skipped == 0 ? "no gardens loaded" : $"{grown} growing";
    }

    // ---------------------------------------------------------------- slimes

    /// <summary>Sloombers near the player that this game runs yawn as soon as they can.</summary>
    internal static string SloombersYawn()
    {
        var position = PlayerPosition();
        if (position == null)
            return "not in a game";

        int count = 0, others = 0;
        foreach (var yawn in Object.FindObjectsOfType<InitiateYawn>())
        {
            if (!yawn || (yawn.transform.position - position.Value).sqrMagnitude > NearbyRadius * NearbyRadius)
                continue;

            if (!OwnedHere(yawn))
            {
                others++;
                continue;
            }

            yawn._cooldownUntilTime = 0;
            count++;
        }

        return Nearby(count, others, "sloomber");
    }

    /// <summary>Dervishes near the player that this game runs spin a cyclone as soon as they can.</summary>
    internal static string DervishesSpin()
    {
        var position = PlayerPosition();
        if (position == null)
            return "not in a game";

        int count = 0, others = 0;
        foreach (var spin in Object.FindObjectsOfType<DervishSlimeSpin>())
        {
            if (!spin || (spin.transform.position - position.Value).sqrMagnitude > NearbyRadius * NearbyRadius)
                continue;

            if (!OwnedHere(spin))
            {
                others++;
                continue;
            }

            spin._nextCycloneTime = 0;
            count++;
        }

        return Nearby(count, others, "dervish");
    }

    private static string Nearby(int count, int others, string what)
    {
        if (count == 0 && others == 0)
            return $"no {what} nearby";

        return others == 0 ? $"{count} ready" : $"{count} ready, {others} run by another player";
    }

    /// <summary>Bursts the gordo the player looks at, as if it had eaten its fill.</summary>
    internal static string BurstGordo()
    {
        var view = CameraTransform();
        if (!view)
            return "not in a game";

        var hits = Physics.RaycastAll(view!.position, view.forward, 60f, ~0, QueryTriggerInteraction.Collide);
        GordoEat? gordo = null;
        var best = float.MaxValue;
        foreach (var hit in hits)
        {
            var found = hit.collider ? hit.collider.GetComponentInParent<GordoEat>() : null;
            if (found && hit.distance < best)
            {
                gordo = found;
                best = hit.distance;
            }
        }

        if (!gordo)
            return "look at a gordo";

        // This game counts as the last feeder, so it drops the rewards and shares them; the
        // others skip theirs when the burst reaches them.
        if (InSession)
            NetworkGordoSlimeManager.MarkLocalFinisher(gordo!.Id);

        gordo!.ImmediateReachedTarget();
        return "burst";
    }
}
