using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.Slime.Dervish;
using Il2CppMonomiPark.SlimeRancher.Slime.Slumber;
using Il2CppMonomiPark.SlimeRancher.Slime.Tangle;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Client.Managers;
using SR2MP.Components.Actor;
using SR2MP.Components.LandPlots;
using SR2MP.Packets.Slime.Sloomber;
using SR2MP.Packets.World;
using SR2MP.Server.Managers;

namespace SR2MP.Shared.Managers;

/// <summary>
/// What the multiplayer cheat menu buttons do. Each returns a short result for the button label.
/// Everything here goes through the game's own code paths, so the existing sync carries it:
/// time skips through the fast-forward sync, weather through the host's forecast, stalks, buds,
/// yawns, cyclones and gordo bursts through their own packets. Things only the host runs
/// (weather, tornados) are asked of the host by a client, and yawns of sloombers this game does
/// not run are asked of their owner.
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

    /// <summary>Set: only this weather. Add: this weather on top of what runs. Clear: no weather.
    /// ClearPattern: one weather type (pattern) off, the others keep running.</summary>
    internal enum WeatherMode : byte
    {
        Set,
        Add,
        Clear,
        ClearPattern,
    }

    /// <summary>Raised when this game's weather may have changed (the host's forecast arrived, or the
    /// host changed or sent its own), so an open cheat menu can update its weather buttons.</summary>
    internal static event Action? WeatherChanged;

    internal static void NotifyWeatherChanged()
    {
        try
        {
            WeatherChanged?.Invoke();
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Cheat menu weather refresh failed: {ex.Message}");
        }
    }

    /// <summary>One weather type (pattern) of a zone: its name, map icon and levels (lowest first).</summary>
    internal sealed class WeatherTypeInfo
    {
        public string Name = string.Empty;
        public Sprite? Icon;
        public List<WeatherStateDefinition> Levels = new();
        public List<string> LevelNames = new();

        public string LevelName(int level)
            => level <= 0 ? "Off" : level <= LevelNames.Count ? LevelNames[level - 1] : "?";
    }

    internal sealed class WeatherTypeHere
    {
        public WeatherPatternDefinition Pattern = null!;
        public WeatherTypeInfo Info = null!;

        // 0 = off, n = Info.Levels[n - 1].
        public int Current;
    }

    private static readonly Dictionary<IntPtr, WeatherTypeInfo> WeatherTypeInfos = new();

    internal static void ForgetWeatherTypes() => WeatherTypeInfos.Clear();

    /// <summary>"Rain Pattern Fields" gives "Rain", "Slime Rain Pattern Valley" gives "Slime Rain". A name
    /// without " Pattern" falls back to the type's analytics name.</summary>
    internal static string PatternLabel(WeatherPatternDefinition pattern)
    {
        var name = pattern.name ?? string.Empty;
        var cut = name.IndexOf(" Pattern", StringComparison.Ordinal);
        if (cut > 0)
            return name[..cut].Trim();

        try
        {
            var metadata = pattern.Metadata;
            if (metadata && !string.IsNullOrEmpty(metadata!.AnalyticsName))
                return metadata.AnalyticsName;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Cheat menu: no type name for {name}: {ex.Message}");
        }

        return name.Trim();
    }

    // The level a state's name says. The names (Light, Med or Medium, Heavy) are what the game uses.
    private static int LevelRank(string label)
        => label.Contains("Light", StringComparison.OrdinalIgnoreCase) ? 1
            : label.Contains("Med", StringComparison.OrdinalIgnoreCase) ? 2
            : label.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ? 3
            : 0;

    private static int MapTierOf(WeatherStateDefinition state)
    {
        try
        {
            return state.MapTier;
        }
        catch
        {
            return 0;
        }
    }

    private static WeatherTypeInfo DescribePattern(WeatherPatternDefinition pattern)
    {
        if (WeatherTypeInfos.TryGetValue(pattern.Pointer, out var known))
            return known;

        var info = new WeatherTypeInfo { Name = PatternLabel(pattern) };
        try
        {
            // The map's weather icon, as WeatherIndicator.Configure shows it.
            var metadata = pattern.Metadata;
            info.Icon = metadata ? metadata!.Icon : null;
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Cheat menu: no icon for {pattern.name}: {ex.Message}");
        }

        NetworkWeatherManager.CheckInitialized();
        var seen = new HashSet<IntPtr>();
        foreach (var state in NetworkWeatherManager.WeatherStates.Values)
        {
            if (state && seen.Add(state.Pointer) && pattern.ContainsState(state.Cast<IWeatherState>()))
                info.Levels.Add(state);
        }

        // A pattern that also lists another type's state keeps to its own type's states.
        var prefix = info.Name + " ";
        if (info.Levels.Any(s => StateLabel(s).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            info.Levels.RemoveAll(s => !StateLabel(s).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        // By the level in the name first; the map tier only breaks ties.
        info.Levels.Sort((a, b) =>
        {
            var byName = LevelRank(StateLabel(a)).CompareTo(LevelRank(StateLabel(b)));
            return byName != 0 ? byName : MapTierOf(a).CompareTo(MapTierOf(b));
        });

        foreach (var state in info.Levels)
        {
            var label = StateLabel(state);
            if (info.Levels.Count == 1)
                label = "On";
            else if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                label = label[prefix.Length..].Trim();

            if (label == "Med")
                label = "Medium";

            info.LevelNames.Add(string.IsNullOrEmpty(label) ? $"Level {MapTierOf(state)}" : label);
        }

        WeatherTypeInfos[pattern.Pointer] = info;
        return info;
    }

    // An entry belongs to a pattern when it names it, or, with no pattern of its own (the host can have
    // such entries), when the pattern has its state.
    private static bool EntryOfPattern(WeatherModel.ForecastEntry entry, WeatherPatternDefinition pattern)
        => entry.Pattern
            ? entry.Pattern.Pointer == pattern.Pointer
            : entry.State != null && pattern.ContainsState(entry.State);

    // The pattern's level now: its newest entry that is due and not ended. A due entry counts before the
    // registry starts it, so the host's own change shows at once. On a client this is the host's forecast.
    private static int CurrentLevel(WeatherRegistry.ZoneWeatherData data, WeatherPatternDefinition pattern,
        List<WeatherStateDefinition> levels, double now)
    {
        var forecast = data.Forecast;
        if (forecast == null)
            return 0;

        var level = 0;
        var newest = double.MinValue;
        foreach (var entry in forecast)
        {
            if (entry == null || entry.State == null || !EntryOfPattern(entry, pattern))
                continue;

            if ((!entry.Started && entry.StartTime > now) || (entry.IsEndTimeKnown && entry.EndTime <= now))
                continue;

            if (entry.StartTime < newest)
                continue;

            var statePointer = entry.State.Pointer;
            var index = levels.FindIndex(s => s.Pointer == statePointer);
            if (index < 0)
                continue;

            newest = entry.StartTime;
            level = index + 1;
        }

        return level;
    }

    /// <summary>The weather types (patterns) of the player's zone, by name, with what each runs now.</summary>
    internal static List<WeatherTypeHere> WeatherTypesHere(out string zoneName)
    {
        var types = new List<WeatherTypeHere>();
        zoneName = string.Empty;

        var zone = CurrentWeatherZone(out var data);
        if (!zone || data == null)
            return types;

        zoneName = zone!.name;
        var patterns = data.Config ? data.Config.Patterns : null;
        if (patterns == null)
            return types;

        var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        var now = time ? time!.WorldTime() : 0d;
        var seen = new HashSet<IntPtr>();
        for (var i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            if (!pattern || !seen.Add(pattern.Pointer))
                continue;

            var info = DescribePattern(pattern);
            if (info.Levels.Count == 0)
                continue;

            types.Add(new WeatherTypeHere { Pattern = pattern, Info = info, Current = CurrentLevel(data, pattern, info.Levels, now) });
        }

        types.Sort((a, b) => string.Compare(a.Info.Name, b.Info.Name, StringComparison.OrdinalIgnoreCase));
        return types;
    }

    internal static bool WeatherChangeDone(string result)
        => result is "added" or "made longer" or "turned off" or "cleared" or "done" or "asked the host";

    /// <summary>One weather type to one level in the player's zone (a null state turns it off); the other
    /// types keep running. The host does it; a client asks.</summary>
    internal static string SetWeatherLevelHere(WeatherPatternDefinition pattern, WeatherStateDefinition? state)
    {
        var zone = CurrentWeatherZone(out _);
        if (!zone)
            return "no weather here";

        if (IsClient)
        {
            var stateId = -1;
            if (state)
            {
                stateId = NetworkWeatherManager.GetPersistentID(state!);
                if (stateId < 0)
                    return "unknown weather";
            }

            Main.Client.SendPacket(new CheatRequestPacket
            {
                Action = state ? CheatRequestPacket.CheatAction.AddWeather : CheatRequestPacket.CheatAction.ClearWeatherPattern,
                Zone = zone!.name,
                StateId = stateId,
                PatternName = pattern.name,
                Hours = WeatherHours,
            });
            return "asked the host";
        }

        return ChangeWeather(zone!, state, WeatherHours, state ? WeatherMode.Add : WeatherMode.ClearPattern, pattern);
    }

    private static bool IsZonePattern(WeatherRegistry.ZoneWeatherData data, WeatherPatternDefinition pattern)
    {
        var patterns = data.Config ? data.Config.Patterns : null;
        if (patterns == null)
            return false;

        for (var i = 0; i < patterns.Count; i++)
        {
            if (patterns[i] && patterns[i].Pointer == pattern.Pointer)
                return true;
        }

        return false;
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

    /// <summary>What runs in the player's zone now, e.g. "Thunder Medium + Wind Light". On a client
    /// this is the host's forecast, as synced.</summary>
    internal static string RunningWeatherHere()
    {
        var zone = CurrentWeatherZone(out var data);
        var forecast = data?.Forecast;
        if (!zone || forecast == null)
            return "no weather zone";

        var time = SceneContext.Instance ? SceneContext.Instance.TimeDirector : null;
        var now = time ? time!.WorldTime() : 0d;
        var names = new List<string>();
        foreach (var entry in forecast)
        {
            if (entry == null || !entry.Started || entry.State == null)
                continue;

            if (entry.IsEndTimeKnown && entry.EndTime <= now)
                continue;

            var def = entry.State.TryCast<WeatherStateDefinition>();
            if (def)
            {
                var label = StateLabel(def!);
                if (!names.Contains(label))
                    names.Add(label);
            }
        }

        return names.Count == 0 ? "clear" : string.Join(" + ", names);
    }

    // The zone's own pattern that has this state (one pattern instance runs one state at a time).
    private static WeatherPatternDefinition? PatternFor(WeatherRegistry.ZoneWeatherData data, IWeatherState state)
    {
        var patterns = data.Config ? data.Config.Patterns : null;
        if (patterns == null)
            return null;

        for (var i = 0; i < patterns.Count; i++)
        {
            var candidate = patterns[i];
            if (candidate && candidate.ContainsState(state))
                return candidate;
        }

        return null;
    }

    /// <summary>Sets, adds or clears weather in the player's zone. The host does it; a client asks.</summary>
    internal static string ApplyWeatherHere(WeatherStateDefinition? state, WeatherMode mode)
    {
        var zone = CurrentWeatherZone(out _);
        if (!zone)
            return "no weather here";

        if (mode == WeatherMode.Clear)
            state = null;
        else if (!state)
            return "pick a weather first";

        if (IsClient)
        {
            // Weather ids start at 0 (Pollen Heavy is 0), so "none" is -1.
            var stateId = state ? NetworkWeatherManager.GetPersistentID(state!) : -1;
            if (state && stateId < 0)
                return "unknown weather";

            Main.Client.SendPacket(new CheatRequestPacket
            {
                Action = mode switch
                {
                    WeatherMode.Add => CheatRequestPacket.CheatAction.AddWeather,
                    WeatherMode.Clear => CheatRequestPacket.CheatAction.ClearWeather,
                    _ => CheatRequestPacket.CheatAction.SetWeather,
                },
                Zone = zone!.name,
                StateId = stateId,
                Hours = WeatherHours,
            });
            return "asked the host";
        }

        return ChangeWeather(zone!, state, WeatherHours, mode);
    }

    /// <summary>Host: a client's request, by the names and ids the weather sync uses.</summary>
    internal static string SetWeatherByIds(string zoneName, int stateId, float hours, WeatherMode mode, string patternName = "")
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

        hours = Mathf.Clamp(hours, 0.25f, 12f);

        // The zone's own pattern the button was for, by the asset name the weather sync uses.
        WeatherPatternDefinition? pattern = null;
        if (!string.IsNullOrEmpty(patternName) && registry._zones.TryGetValue(zone!, out var data) && data != null)
        {
            var patterns = data.Config ? data.Config.Patterns : null;
            for (var i = 0; patterns != null && i < patterns.Count; i++)
            {
                if (patterns[i] && patterns[i].name == patternName)
                {
                    pattern = patterns[i];
                    break;
                }
            }
        }

        if (mode == WeatherMode.ClearPattern)
            return pattern ? ChangeWeather(zone!, null, hours, mode, pattern) : $"no weather type '{patternName}' in {zoneName}";

        // "Set nothing" means clear skies. Ids start at 0 (a real weather), so nothing is -1.
        if (mode == WeatherMode.Set && stateId < 0)
            mode = WeatherMode.Clear;

        WeatherStateDefinition? state = null;
        if (mode != WeatherMode.Clear)
        {
            NetworkWeatherManager.CheckInitialized();
            if (!NetworkWeatherManager.WeatherStates.TryGetValue(stateId, out state) || !state)
                return $"unknown weather {stateId}";
        }

        return ChangeWeather(zone!, state, hours, mode, pattern);
    }

    /// <summary>
    /// Host or alone: changes the zone's forecast the way the game's own forecast does. Each pattern
    /// (Rain, Thunder, Wind, ...) runs one state at a time and patterns run side by side, which is how
    /// the game stacks weather.
    /// Set ends everything running now, drops upcoming weather inside the window, and adds this weather.
    /// Add touches only this weather's own pattern (and entries of the same state): it ends that
    /// pattern's running state and adds this one, and other patterns keep running.
    /// Clear is Set with nothing added. ClearPattern ends one pattern's weather, running or upcoming in the
    /// window, and leaves the other patterns alone.
    /// The registry stops and starts the states on its next update, and the weather sync passes that on.
    /// </summary>
    internal static string ChangeWeather(ZoneDefinition zone, WeatherStateDefinition? state, float hours, WeatherMode mode,
        WeatherPatternDefinition? forPattern = null)
    {
        var scene = SceneContext.Instance;
        var registry = scene ? scene.WeatherRegistry : null;
        var time = scene ? scene.TimeDirector : null;
        if (!registry || !time || registry!._zones == null)
            return "not in a game";

        if (!registry._zones.TryGetValue(zone, out var data) || data == null)
            return "no weather here";

        if (mode is WeatherMode.Clear or WeatherMode.ClearPattern)
            state = null;
        else if (!state)
            return "pick a weather first";

        WeatherPatternDefinition? pattern = null;
        IWeatherState? asState = null;
        if (state)
        {
            asState = state!.Cast<IWeatherState>();

            // The type the button is for, when it is this zone's and has the state; otherwise the zone's own.
            pattern = forPattern && IsZonePattern(data, forPattern!) && forPattern!.ContainsState(asState)
                ? forPattern
                : PatternFor(data, asState);
            if (!pattern)
                return $"{zone.name} has no {StateLabel(state)}";
        }
        else if (mode == WeatherMode.ClearPattern)
        {
            if (!forPattern || !IsZonePattern(data, forPattern!))
                return $"{zone.name} has no such weather";

            pattern = forPattern;
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

            var samePattern = pattern && EntryOfPattern(entry, pattern!);

            // The director runs a state once per start without checking for a copy, so the same state
            // in another pattern has to end too, or it would run twice.
            var sameState = asState != null && entry.State != null && entry.State.Pointer == asState.Pointer;

            // Add and turning one type off leave the other patterns' weather alone, running or upcoming,
            // so weather stacks.
            if ((mode is WeatherMode.Add or WeatherMode.ClearPattern) && !samePattern && !sameState)
                continue;

            if (entry.Started)
            {
                // The same weather already running just goes on longer.
                if (samePattern && sameState)
                {
                    entry.EndTime = Math.Max(entry.IsEndTimeKnown ? entry.EndTime : end, end);
                    entry.IsEndTimeKnown = true;
                    extended = true;
                    continue;
                }

                // Ended on the registry's next update, which stops it. A pattern's running state must
                // end now even in Add: one pattern instance holds one state, and an old entry ending
                // later would exit and remove the instance under the new state.
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

        // A start sends a correct forecast by itself, but a stop is sent before the registry takes the
        // ended entry out. Clear has no start after its stops, and making a weather longer neither stops
        // nor starts anything, so send the forecast again once the registry has updated.
        if (Main.Server.IsRunning)
        {
            Later(WeatherUpdateHelper.SendWeatherUpdate, 3);
            Later(WeatherUpdateHelper.SendWeatherUpdate, 30);
        }

        var what = state ? $"{StateLabel(state!)} for {hours:0.#} h"
            : mode == WeatherMode.ClearPattern ? $"{PatternLabel(pattern!)} off for {hours:0.#} h"
            : "clear skies";
        SrLogger.LogMessage($"Cheat menu: weather in {zone.name}: {mode} {what}{(extended ? " (made longer)" : string.Empty)}");
        if (extended)
            return "made longer";

        return mode switch
        {
            WeatherMode.Add => "added",
            WeatherMode.Clear => "cleared",
            WeatherMode.ClearPattern => "turned off",
            _ => "done",
        };
    }

    // ---------------------------------------------------------------- tornado

    // SlimenadoMoveSpiral (SR2 1.3.0) checks every 0.5 s, from _groundHeightAdjustUp above its base: a ray
    // ahead along its last step and a ray down (adjustUp + adjustDown long), both on Default + Gadget
    // (layer 27). Either failing fades it. So the cheat picks a spot and heading whose simulated path
    // passes those same checks.
    private const int TornadoGroundMask = 0x8000001;   // SlimenadoMoveSpiral.GROUND_RAY_MASK
    private const float TornadoCheckPeriod = 0.5f;     // GROUND_CHECK_PERIOD
    private const float TornadoInitialSpin = 90f;      // INIT_ROT (degrees per second)
    private const float TornadoSpinDecay = 0.9f;       // _rot *= 0.9^dt
    private const float TornadoVerticalSpeed = 5f;     // VERT_SPEED
    private const float TornadoDefaultAdjust = 10f;    // SlimenadoMoveSpiral's own default for both adjust fields

    // A spot is taken at once when its simulated tornado lasts this long, and refused below the minimum.
    private const float TornadoTestSeconds = 20f;
    private const float TornadoMinSeconds = 10f;

    // A cheat tornado lasts at least this many game hours (a game hour is a real minute), even when the
    // activity's own random lifetime is shorter.
    private const float TornadoCheatMinHours = 1f;

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
        var rotation = Quaternion.LookRotation(forward);

        if (IsClient)
        {
            // The host may have no colliders where this player stands (it only has them near its own
            // player), so this game tests the spot with its own physics and sends the tested path.
            var activity = TornadoActivity();
            if (activity == null)
                return "no tornado found in the game";

            if (!FindTornadoSpot(activity, spot, rotation.eulerAngles.y, out var tested, out var yaw, out var speed, out var life))
                return "no open ground for a tornado there";

            Main.Client.SendPacket(new CheatRequestPacket
            {
                Action = CheatRequestPacket.CheatAction.Tornado,
                Position = tested,
                Rotation = Quaternion.Euler(0f, yaw, 0f),
                Speed = speed,
            });
            SrLogger.LogMessage($"Cheat menu: tornado spot {tested}, heading {yaw * 2f % 360f:0} degrees, "
                                + $"speed {speed:0.#}, path tested clear here for {life:0.#} s");
            return "asked the host";
        }

        return SpawnTornadoAt(spot, rotation);
    }

    /// <summary>Host or alone: the weather's own tornado spawn, placed where the tornado's own ground
    /// and obstacle checks let it live. The tornado sync shares it.</summary>
    internal static string SpawnTornadoAt(Vector3 position, Quaternion rotation)
    {
        var activity = TornadoActivity();
        if (activity == null)
            return "no tornado found in the game";

        if (!FindTornadoSpot(activity, position, rotation.eulerAngles.y, out var spot, out var yaw, out var speed, out var life))
            return "no open ground for a tornado there";

        return SpawnTestedTornado(activity, spot, yaw, speed, life, position);
    }

    /// <summary>Host: a client's tornado request, with the spot, heading and speed the client tested
    /// with its own physics. The host runs it as before when its own player is near and the same path
    /// passes here too. Otherwise the host has no colliders there, so the requesting player's game runs
    /// it for everyone and the host only gives it an id and passes its state on.</summary>
    internal static string SpawnRequestedTornado(Vector3 spot, Quaternion rotation, float speed, IPEndPoint? requester)
    {
        // A request without a tested path (no speed) is placed by the host, as before.
        if (speed <= 0f || requester == null)
            return SpawnTornadoAt(spot, rotation);

        var activity = TornadoActivity();
        if (activity == null)
            return "no tornado found in the game";

        var yaw = rotation.eulerAngles.y;
        TornadoAdjust(activity, out var up, out var down);
        var life = TornadoLife(spot, yaw, speed, up, down, TornadoTestSeconds);
        if (life >= TornadoMinSeconds && NetworkTornadoManager.PlayerInDistance(spot))
            return SpawnTestedTornado(activity, spot, yaw, speed, life, spot);

        var id = NetworkTornadoManager.GrantToRequester(requester, spot, rotation, speed, TornadoCheatMinHours);
        SrLogger.LogMessage($"Cheat menu: tornado {id} at {spot}: not near the host here (path clear {life:0.#} s), so {requester}'s game runs it");
        return "handed to the requesting player";
    }

    private static string SpawnTestedTornado(SpawnTornadoActivity activity, Vector3 spot, float yaw, float speed, float life, Vector3 askedFor)
    {
        // Read by OnTornadoSpawn.Postfix before it shares the tornado.
        NetworkTornadoManager.NextSpeed = speed;
        NetworkTornadoManager.NextMinLifetimeHours = TornadoCheatMinHours;
        GameObject tornado;
        try
        {
            tornado = activity.Spawn(spot, Quaternion.Euler(0f, yaw, 0f));
        }
        finally
        {
            NetworkTornadoManager.NextSpeed = -1f;
            NetworkTornadoManager.NextMinLifetimeHours = 0f;
        }

        if (!tornado)
            return "the game made no tornado";

        SrLogger.LogMessage($"Cheat menu: tornado at {spot} (asked for {askedFor}), heading {yaw * 2f % 360f:0} degrees, "
                            + $"speed {speed:0.#}, path tested clear for {life:0.#} s");
        return "spawned";
    }

    /// <summary>A tornado activity whose Prefab is set (some instances in memory have none).</summary>
    internal static SpawnTornadoActivity? TornadoActivity()
    {
        var last = NetworkTornadoManager.LastActivity;
        if (last != null && last.Prefab)
            return last;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<SpawnTornadoActivity>())
        {
            if (candidate && candidate.Prefab)
                return candidate;
        }

        return null;
    }

    private static bool GroundBelow(Vector3 column, float checkHeight, out RaycastHit hit)
    {
        // First from the tornado's own check height, which stays under cave roofs, overhangs and tree
        // tops; then from high up, for ground that rises ahead of the player.
        var low = Mathf.Max(checkHeight, 4f);
        return Physics.Raycast(column + Vector3.up * low, Vector3.down, out hit, low + 30f, TornadoGroundMask, QueryTriggerInteraction.Ignore)
               || Physics.Raycast(column + Vector3.up * 40f, Vector3.down, out hit, 120f, TornadoGroundMask, QueryTriggerInteraction.Ignore);
    }

    // The mover's ground check heights, from the activity's tornado prefab.
    private static void TornadoAdjust(SpawnTornadoActivity activity, out float up, out float down)
    {
        up = TornadoDefaultAdjust;
        down = TornadoDefaultAdjust;
        var prefab = activity.Prefab;
        var mover = prefab ? prefab.GetComponent<SlimenadoMoveSpiral>() : null;
        if (mover)
        {
            up = mover!._groundHeightAdjustUp;
            down = mover._groundHeightAdjustDown;
        }
    }

    /// <summary>Tries the requested spot, then rings around it, and several headings for each spot,
    /// until the game's own mover would survive TornadoTestSeconds there.</summary>
    private static bool FindTornadoSpot(SpawnTornadoActivity activity, Vector3 around, float lookYaw,
        out Vector3 spot, out float yaw, out float speed, out float bestLife)
    {
        spot = around;
        yaw = 0f;
        bestLife = 0f;
        speed = UnityEngine.Random.Range(Mathf.Min(activity.MinSpeed, activity.MaxSpeed),
                                         Mathf.Max(activity.MinSpeed, activity.MaxSpeed));

        TornadoAdjust(activity, out var up, out var down);

        // The mover travels at twice its yaw (it translates its forward in its own space), so yaws over
        // half a turn cover every heading; start with the one that heads away from the player.
        var firstYaw = lookYaw * 0.5f;
        for (var ring = 0; ring <= 3; ring++)
        {
            var count = ring == 0 ? 1 : 8;
            for (var i = 0; i < count; i++)
            {
                var offset = Quaternion.Euler(0f, lookYaw + i * 360f / count, 0f) * Vector3.forward * (ring * 8f);
                if (!GroundBelow(around + offset, up, out var ground))
                    continue;   // nothing loaded here (for example the host has not loaded that area)

                if (ground.normal.y < 0.5f)
                    continue;   // cliff face

                for (var k = 0; k < 6; k++)
                {
                    var tryYaw = firstYaw + k * 30f;
                    var life = TornadoLife(ground.point, tryYaw, speed, up, down, TornadoTestSeconds);
                    if (life > bestLife)
                    {
                        bestLife = life;
                        spot = ground.point;
                        yaw = tryYaw;
                    }

                    if (life >= TornadoTestSeconds)
                        return true;
                }
            }
        }

        return bestLife >= TornadoMinSeconds;
    }

    /// <summary>Replays SlimenadoMoveSpiral.FixedUpdate's movement and checks from a spot, and returns
    /// how many seconds the tornado would last before its own checks fade it.</summary>
    private static float TornadoLife(Vector3 start, float yaw, float speed, float up, float down, float seconds)
    {
        var dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
        var pos = start;
        var targetY = start.y;
        var spin = TornadoInitialSpin;
        var prior = pos + Vector3.up * up;
        var nextCheck = 0f;
        var decay = Mathf.Pow(TornadoSpinDecay, dt);

        for (var t = 0f; t < seconds; t += dt)
        {
            pos.y = pos.y < targetY
                ? Mathf.Min(targetY, pos.y + TornadoVerticalSpeed * dt)
                : Mathf.Max(targetY, pos.y - TornadoVerticalSpeed * dt);
            var turn = Quaternion.Euler(0f, yaw, 0f);
            pos += turn * (turn * Vector3.forward) * (speed * dt);   // Translate(forward) in its own space
            yaw += spin * dt;
            spin *= decay;

            if (t < nextCheck)
                continue;

            nextCheck = t + TornadoCheckPeriod;

            var from = pos + Vector3.up * up;
            var step = from - prior;
            prior = from;
            var length = step.magnitude;
            if (length > 0.001f && Physics.Raycast(from, step / length, length, TornadoGroundMask, QueryTriggerInteraction.Ignore))
                return t;   // blocked

            if (!Physics.Raycast(from, Vector3.down, out var hit, up + down, TornadoGroundMask, QueryTriggerInteraction.Ignore))
                return t;   // no ground

            targetY = hit.point.y;
        }

        return seconds;
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
    /// Grows a tangle bud stalk on the ground the player looks at, with only a short seed wait. The
    /// stalk is this game's own, so the tangle sync shows it to the others and the bud is a normal
    /// shared actor.
    /// </summary>
    private const float TangleSeedSeconds = 0.5f;

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

        // With a seed phase, as the game grows them: without one the stalk animates in before its
        // animator's own Start, which then hides the plant again, so nothing showed.
        var regions = scene!.RegionRegistry;
        stalk.Init(regions ? regions!.CurrentSceneGroup : null, true, TangleSeedSeconds, false);
        return $"growing {hit.distance:0} m away";
    }

    // ---------------------------------------------------------------- gardens

    /// <summary>
    /// The button: every game grows the gardens it runs and ripens the produce it runs, so one press
    /// covers every ranch garden whoever runs it. This game runs its part now. A client asks the host,
    /// which runs its part and tells the other clients to run theirs (CheatRun); a host tells every client.
    /// </summary>
    internal static string GrowRanchGardensForAll()
    {
        var result = GrowRanchGardens(out _);

        if (Main.Server.IsRunning)
        {
            Main.Server.SendToAll(new CheatRunPacket { Action = CheatRunPacket.CheatAction.GrowGardens });
            return result + ", sent to all";
        }

        if (IsClient)
        {
            Main.Client.SendPacket(new CheatRequestPacket { Action = CheatRequestPacket.CheatAction.GrowGardens });
            return result + ", asked the host";
        }

        return result;
    }

    /// <summary>
    /// This game's part of the garden cheat. Ranch gardens: unripe produce this game runs ripens now (per produce actor, whoever runs the
    /// garden), and gardens this game runs fill every empty spot with ripe produce right away through
    /// the game's own ripe spawn. New produce and ripeness reach the others through the actor spawn,
    /// ResourceAttach and actor state sync.
    /// </summary>
    internal static string GrowRanchGardens(out int skipped)
    {
        skipped = 0;
        var scene = SceneContext.Instance;
        var time = scene ? scene.TimeDirector : null;
        if (!time)
            return "not in a game";

        var now = time!.WorldTime();
        int gardens = 0, spawned = 0, ripened = 0, othersProduce = 0, planting = 0, full = 0, asleep = 0;

        foreach (var spawner in Object.FindObjectsOfType<SpawnResource>())
        {
            if (!spawner || !spawner.GetComponentInParent<LandPlot>())
                continue;

            var joints = spawner.SpawnJoints;
            if (joints == null || joints.Length == 0)
                continue;

            // 1. Ripen the produce hanging on the plant, read from the joints (SpawnResource._spawned
            //    does not list produce loaded from a save, and nothing at all when the grower has no
            //    active-spawn cap). Gated per produce actor, not per garden: produce changes owner by
            //    player proximity, so the garden's owner often does not run its fruit.
            ripened += RipenAttachedProduce(spawner, ref othersProduce);

            // 2. Spawning only on the game that runs the garden (the others have it switched off).
            if (InSession)
            {
                var garden = spawner.GetComponent<NetworkGarden>();
                if (!garden || !garden!.LocallyOwned)
                {
                    if (garden && garden!.OwnedHere)
                        asleep++;   // ours, but its area sleeps, so the game is not running it now
                    else
                        skipped++;
                    continue;
                }
            }

            var model = spawner._model;
            if (model == null || !spawner.isActiveAndEnabled)
                continue;

            gardens++;

            // A plot still scaling in after planting holds a spawn blocker until the tween ends: let the
            // game's own timer spawn its first batch ripe right after.
            if (spawner._spawnBlockers > 0)
            {
                model.nextSpawnRipens = true;
                if (model.nextSpawnTime > now)
                    model.nextSpawnTime = now;
                planting++;
                continue;
            }

            // 3. Fill every empty joint with ripe produce now. Spawn(SpawnRequest) skips the hour window
            //    and the timer; SpawnRipe makes it call ProgressResource(now) on each new fruit, so it is
            //    ripe at once. One call spawns a random-sized batch, so repeat while it still fills joints.
            var free = FreeGardenJoints(spawner);
            if (free == 0)
            {
                full++;
                continue;
            }

            for (var pass = 0; free > 0 && pass < joints.Length; pass++)
            {
                try
                {
                    spawner.Spawn(new SpawnResource.SpawnRequest { SpawnRipe = true });
                }
                catch (Exception ex)
                {
                    SrLogger.LogDebug($"Cheat menu: garden spawn failed: {ex.Message}");
                    break;
                }

                if (!spawner)
                    break;   // the grower destroyed itself (lifetime spawn limit)

                var after = FreeGardenJoints(spawner);
                if (after >= free)
                    break;   // the game will not spawn more here now (active-spawn cap)

                spawned += free - after;
                free = after;
            }
        }

        SrLogger.LogMessage($"Cheat menu: gardens {gardens} run here (+{skipped} by another player, {asleep} asleep): "
                            + $"{spawned} new ripe produce, {ripened} ripened, {full} already full, "
                            + $"{planting} still planting, {othersProduce} unripe produce run by another player");

        if (gardens == 0 && ripened == 0)
        {
            if (skipped == 0 && asleep == 0)
                return "no gardens loaded";

            if (skipped == 0)
                return $"{asleep} gardens asleep";

            return othersProduce > 0
                ? $"{skipped} gardens, {othersProduce} produce run by another player"
                : $"{skipped} gardens run by another player";
        }

        var result = $"{spawned} new, {ripened} ripened";
        if (full > 0 && spawned == 0 && ripened == 0)
            result = "already full and ripe";
        if (planting > 0)
            result += $", {planting} planting";
        if (skipped > 0 || othersProduce > 0)
            result += $", {skipped + othersProduce} others'";
        if (asleep > 0)
            result += $", {asleep} asleep";
        return result;
    }

    /// <summary>Unripe produce on the spawner's joints that this game runs ripens now.</summary>
    private static int RipenAttachedProduce(SpawnResource spawner, ref int others)
    {
        var count = 0;
        var joints = spawner.SpawnJoints;
        for (var i = 0; i < joints.Length; i++)
        {
            var joint = joints[i];
            var body = joint ? joint!.connectedBody : null;
            var cycle = body ? body!.GetComponent<ResourceCycle>() : null;
            if (!cycle || cycle!._model == null || cycle._model.state != ResourceCycle.State.UNRIPE)
                continue;

            // Non-owners freeze progressTime every frame; only the produce's owner can ripen it.
            if (!OwnedHere(cycle))
            {
                others++;
                continue;
            }

            try
            {
                // progressTime = now; the next ResourceCycle update runs Ripen() (full size, vacuumable)
                // and the owner's actor state sync sends RIPE to the others.
                cycle.ImmediatelyRipen(0f);
                count++;
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Cheat menu: ripen failed: {ex.Message}");
            }
        }

        return count;
    }

    /// <summary>Joints with nothing attached: the same test SpawnResource.Spawn uses.</summary>
    private static int FreeGardenJoints(SpawnResource spawner)
    {
        var free = 0;
        var joints = spawner.SpawnJoints;
        for (var i = 0; i < joints.Length; i++)
        {
            var joint = joints[i];
            if (joint && !joint!.connectedBody)
                free++;
        }

        return free;
    }

    // ---------------------------------------------------------------- slimes

    /// <summary>Every sloomber near the player yawns, for everyone: this game's own ones now, the
    /// others are asked of their owner (a client asks the host, which passes it on).</summary>
    internal static string SloombersYawn()
    {
        var position = PlayerPosition();
        if (position == null)
            return "not in a game";

        int count = 0, asked = 0;
        foreach (var yawn in Object.FindObjectsOfType<InitiateYawn>())
        {
            if (!yawn || (yawn.transform.position - position.Value).sqrMagnitude > NearbyRadius * NearbyRadius)
                continue;

            if (OwnedHere(yawn))
            {
                YawnNow(yawn);
                count++;
                continue;
            }

            var actor = yawn.GetComponentInParent<NetworkActor>();
            var actorId = actor ? actor!.ActorId.Value : 0;
            if (actorId == 0)
                continue;

            Main.SendToAllOrServer(new SloomberYawnPacket { ActorId = new ActorId(actorId), Request = true });
            asked++;
        }

        if (count == 0 && asked == 0)
            return "no sloomber nearby";

        return $"{count} yawning, {asked} asked of their owner";
    }

    /// <summary>Owner only: the sloomber yawns through the game's own code, so the yawn sync sends
    /// it. A napping one yawns in its sleep on its next physics tick (SlimeNap.Action starts a yawn
    /// once the cooldown has passed); an awake one is switched to its yawn behaviour now when its
    /// current behaviour may be interrupted, otherwise it yawns when that ends.</summary>
    internal static void YawnNow(InitiateYawn yawn)
    {
        yawn._cooldownUntilTime = 0;
        try
        {
            // The field InitiateYawn.Awake sets.
            var nap = yawn._slimeNap;
            if (!nap)
                nap = yawn.GetComponent<SlimeNap>();
            if (nap && nap!._selected)
                return;

            if (yawn._state != InitiateYawn.State.IDLE)
                return;

            var plexer = yawn.Plexer;
            if (!plexer || plexer.IsCaptive() || plexer.IsBehaviorBlocked())
                return;

            var current = plexer.CurrentBehavior;
            if (current && (current!.Pointer == yawn.Pointer || !current.CanRethink()))
                return;

            // Deselects the current behaviour and runs InitiateYawn.Selected, whose patch sends the wind-up.
            plexer.ForceBehavior(yawn);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Cheat menu: starting a yawn failed, it yawns when it can: {ex.Message}");
        }
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
