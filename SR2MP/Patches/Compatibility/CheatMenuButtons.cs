using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Starlight.Buttons;
using Starlight.Enums.Sounds;
using Starlight.Menus;
using SR2MP.Shared.Managers;
using UnityEngine.UI;

namespace SR2MP.Patches.Compatibility;

/// <summary>
/// Extra buttons in Starlight's cheat menu: the ranch house rests, one button per weather type, and
/// things that are slow or random to make happen when testing (tangle bud stalks, garden growth,
/// sloomber yawns, dervish cyclones, gordo bursts, tornados). They work alone and in multiplayer, where
/// the host's cheat setting decides, and they go through the game's own code so the sync carries them.
/// </summary>
[HarmonyPatch(typeof(StarlightCheatMenu), "OnOpen")]
internal static class CheatMenuButtons
{
    private sealed class Entry
    {
        public StarlightCheatMenuButton Button = null!;
        public Func<string> Label = null!;

        // Shown after the label until the menu closes, e.g. what the last press did.
        public string Result = string.Empty;

        // Null: always shown.
        public Func<bool>? Visible;

        // Null: no icon.
        public Func<Sprite?>? Icon;

        // A weather type slot.
        public bool Weather;
    }

    private static readonly List<Entry> Entries = new();
    private static bool registered;

    // One button per weather type of the zone (Rain, Thunder, Wind, Pollen, Slime Rain, Snow). The zones
    // have five at most.
    private const int WeatherSlots = 6;
    private static List<NetworkCheats.WeatherTypeHere> weatherTypes = new();
    private static string weatherZone = string.Empty;
    private static readonly HashSet<string> LoggedOverflow = new();

    // Client: the level last asked for, per type, shown with "..." until the host's weather has it.
    private static readonly Dictionary<IntPtr, (int Level, float At)> PendingLevels = new();
    private const float PendingSeconds = 5f;

    // While the menu is open the weather labels follow the weather: the host's sync and the registry
    // starting a due entry. The zone itself cannot change then, as Starlight overrides the input.
    private const int PollFrames = 15;
    private static int openGeneration;
    private static int layoutLoggedGeneration;

    private const string IconName = "SR2MP Weather Icon";

    private static StarlightCheatMenu? Menu => MenuEUtil.GetMenu<StarlightCheatMenu>();

    private static bool MenuOpen
    {
        get
        {
            var menu = Menu;
            return menu && menu!.isOpen;
        }
    }

    // Starlight's own list of cheat buttons (internal to Starlight).
    private static List<StarlightCheatMenuButton>? StarlightButtons()
        => AccessTools.Field(typeof(StarlightCheatMenu), "cheatButtons")?.GetValue(null) as List<StarlightCheatMenuButton>;

    // Added on the first open, so they come after Starlight's own buttons.
    public static void Prefix()
    {
        try
        {
            if (!registered)
            {
                registered = true;
                Register();
            }

            foreach (var entry in Entries)
                entry.Result = string.Empty;

            NetworkCheats.ForgetWeatherTypes();
            RefreshWeatherTypes();
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Cheat menu buttons: {ex}");
        }
    }

    public static void Postfix()
    {
        try
        {
            foreach (var entry in Entries)
                TryRefresh(entry);

            SchedulePoll(++openGeneration);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Cheat menu buttons: {ex}");
        }
    }

    private static void Register()
    {
        foreach (var until in new[]
                 {
                     NetworkCheats.RestUntil.Morning, NetworkCheats.RestUntil.Noon,
                     NetworkCheats.RestUntil.Evening, NetworkCheats.RestUntil.Midnight,
                 })
        {
            var restUntil = until;
            Add(() => NetworkCheats.RestLabel(restUntil), () => NetworkCheats.Rest(restUntil), closeFirst: true);
        }

        // Starlight refuses a label it already has, but only when a button is made, so the slots are made
        // with fixed unique labels and show the zone's weather types afterwards. Unused slots are hidden.
        for (var i = 0; i < WeatherSlots; i++)
        {
            var slot = i;
            var entry = Add(() => WeatherSlotLabel(slot), () => CycleWeather(slot),
                registerLabel: WeatherSlotName(slot),
                visible: () => slot < weatherTypes.Count || slot == 0,
                icon: () => slot < weatherTypes.Count ? weatherTypes[slot].Info.Icon : null);
            if (entry != null)
                entry.Weather = true;
        }

        Add(() => "Clear all weather here", ClearWeatherHere);
        Add(() => "Spawn a tornado ahead", NetworkCheats.TornadoHere);
        Add(() => "Grow a tangle bud stalk where I look", NetworkCheats.GrowTangleStalk);
        Add(() => "Grow and ripen ranch gardens now", NetworkCheats.GrowRanchGardensForAll);
        Add(() => "Make nearby sloombers yawn", NetworkCheats.SloombersYawn);
        Add(() => "Make nearby dervishes spin a cyclone", NetworkCheats.DervishesSpin);
        Add(() => "Burst the gordo I look at", NetworkCheats.BurstGordo);

        NetworkCheats.WeatherChanged += OnWeatherChanged;
    }

    private static Entry? Add(Func<string> label, Func<string> action, bool closeFirst = false, bool needsCheats = true,
        string? registerLabel = null, Func<bool>? visible = null, Func<Sprite?>? icon = null)
    {
        var entry = new Entry { Label = label, Visible = visible, Icon = icon };
        entry.Button = new StarlightCheatMenuButton(registerLabel ?? label(), () => Press(entry, action, closeFirst, needsCheats));

        // Starlight logs and leaves out a button whose label another button already has.
        if (StarlightButtons()?.Contains(entry.Button) ?? false)
        {
            Entries.Add(entry);
            return entry;
        }

        SrLogger.LogWarning($"Cheat menu: button '{entry.Button.Label}' was not added");
        return null;
    }

    private static void Press(Entry entry, Func<string> action, bool closeFirst, bool needsCheats)
    {
        try
        {
            if (needsCheats && !NetworkCheats.Allowed)
            {
                AudioEUtil.PlaySound(MenuSound.Error);
                entry.Result = "cheats are off on this server";
                Refresh(entry);
                return;
            }

            AudioEUtil.PlaySound(MenuSound.Click);

            if (closeFirst)
            {
                // Things that move time or hold the player wait until the menu has closed and
                // unpaused the game.
                var menu = Menu;
                if (menu)
                    menu!.Close();

                NetworkCheats.Later(() => Run(entry, action), 2);
                return;
            }

            Run(entry, action);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Cheat menu button '{entry.Button.Label}' failed: {ex}");
        }
    }

    private static void Run(Entry entry, Func<string> action)
    {
        try
        {
            entry.Result = action() ?? string.Empty;
        }
        catch (Exception ex)
        {
            entry.Result = "failed, see the log";
            SrLogger.LogWarning($"Cheat menu button '{entry.Button.Label}' failed: {ex}");
        }

        // A button that closed the menu first has no label to show this on.
        SrLogger.LogMessage($"Cheat menu: {entry.Button.Label}: {entry.Result}");
        Refresh(entry);
    }

    // One button failing to refresh does not stop the others.
    private static void TryRefresh(Entry entry)
    {
        try
        {
            Refresh(entry);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Cheat menu button '{entry.Button.Label}' refresh failed: {ex}");
        }
    }

    private static void Refresh(Entry entry)
    {
        var text = entry.Label();

        // Starlight shows this label on the next open; it checks labels only when a button is made.
        entry.Button.Label = text;

        if (!string.IsNullOrEmpty(entry.Result))
            text = $"{text}: {entry.Result}";

        if (!MenuOpen)
            return;

        var button = entry.Button.ButtonInstance;
        if (!button)
            return;

        if (entry.Visible != null)
        {
            var show = entry.Visible();
            if (button!.gameObject.activeSelf != show)
                button.gameObject.SetActive(show);
        }

        if (entry.Icon != null)
            ShowIcon(entry, entry.Icon());

        var label = entry.Button.TextInstance;
        if (label && label!.text != text)
            label.text = text;
    }

    // ---------------------------------------------------------------- weather types

    private static string WeatherSlotName(int slot) => $"Weather type {slot + 1}";

    private static void RefreshWeatherTypes()
    {
        var before = weatherZone;
        weatherTypes = NetworkCheats.WeatherTypesHere(out weatherZone);

        if (weatherTypes.Count > WeatherSlots && LoggedOverflow.Add(weatherZone))
            SrLogger.LogWarning($"Cheat menu: {weatherZone} has {weatherTypes.Count} weather types, only {WeatherSlots} shown");

        if (before == weatherZone)
            return;

        PendingLevels.Clear();
        foreach (var entry in Entries)
        {
            if (entry.Weather)
                entry.Result = string.Empty;
        }
    }

    private static void RefreshWeatherSlots()
    {
        RefreshWeatherTypes();
        foreach (var entry in Entries)
        {
            if (entry.Weather)
                TryRefresh(entry);
        }
    }

    private static void OnWeatherChanged()
    {
        if (registered && MenuOpen)
            RefreshWeatherSlots();
    }

    private static int ShownLevel(NetworkCheats.WeatherTypeHere type)
    {
        var key = type.Pattern.Pointer;
        if (!PendingLevels.TryGetValue(key, out var pending))
            return type.Current;

        if (pending.Level == type.Current || UnityEngine.Time.realtimeSinceStartup - pending.At > PendingSeconds)
        {
            PendingLevels.Remove(key);
            return type.Current;
        }

        return pending.Level;
    }

    private static string WeatherSlotLabel(int slot)
    {
        if (slot < weatherTypes.Count)
        {
            var type = weatherTypes[slot];
            var level = ShownLevel(type);
            var text = $"{type.Info.Name}: {type.Info.LevelName(level)}";
            return level != type.Current ? text + " ..." : text;
        }

        if (slot == 0)
            return string.IsNullOrEmpty(weatherZone) ? "Weather: not in a weather zone" : "Weather: none in this zone";

        // Hidden, and still unique.
        return WeatherSlotName(slot);
    }

    // Off, Light, Medium, Heavy, then Off again, for this type only; the others keep running. A type with
    // one state goes Off, On, Off.
    private static string CycleWeather(int slot)
    {
        RefreshWeatherTypes();
        if (slot >= weatherTypes.Count)
            return string.IsNullOrEmpty(weatherZone) ? "no weather here" : "no weather in this zone";

        var type = weatherTypes[slot];
        var from = ShownLevel(type);
        var to = (from + 1) % (type.Info.Levels.Count + 1);
        var state = to == 0 ? null : type.Info.Levels[to - 1];

        var result = NetworkCheats.SetWeatherLevelHere(type.Pattern, state);
        var ok = NetworkCheats.WeatherChangeDone(result);
        SrLogger.LogMessage($"Cheat menu: {type.Info.Name} in {weatherZone}: {type.Info.LevelName(from)} -> {type.Info.LevelName(to)}: {result}");

        if (ok)
            PendingLevels[type.Pattern.Pointer] = (to, UnityEngine.Time.realtimeSinceStartup);

        RefreshWeatherSlots();
        return ok ? string.Empty : result;
    }

    private static string ClearWeatherHere()
    {
        var result = NetworkCheats.ApplyWeatherHere(null, NetworkCheats.WeatherMode.Clear);
        PendingLevels.Clear();
        if (NetworkCheats.WeatherChangeDone(result))
        {
            foreach (var type in weatherTypes)
            {
                if (type.Current != 0)
                    PendingLevels[type.Pattern.Pointer] = (0, UnityEngine.Time.realtimeSinceStartup);
            }
        }

        RefreshWeatherSlots();
        return result;
    }

    // A new closure each time, as Starlight keys its pending actions by the delegate.
    private static void SchedulePoll(int generation)
        => NetworkCheats.Later(() => Poll(generation), PollFrames);

    private static void Poll(int generation)
    {
        // A newer open has its own poll, and a closed menu needs none.
        if (generation != openGeneration || !MenuOpen)
            return;

        try
        {
            LogLayout(generation);
            RefreshWeatherSlots();
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Cheat menu weather refresh failed: {ex.Message}");
        }

        SchedulePoll(generation);
    }

    // The game's weather icon (the map's) at the left of the button. Starlight makes the buttons again on
    // every open and destroys them on close, so the icon is added to each new one.
    private static void ShowIcon(Entry entry, Sprite? sprite)
    {
        var button = entry.Button.ButtonInstance;
        if (!button)
            return;

        var root = button!.transform;
        var found = root.Find(IconName);
        var image = found ? found!.GetComponent<Image>() : null;

        if (!sprite)
        {
            if (image && image!.gameObject.activeSelf)
                image.gameObject.SetActive(false);
            return;
        }

        if (!image)
        {
            var go = new GameObject(IconName) { layer = button.gameObject.layer };
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0f, 0.12f);
            rect.anchorMax = new Vector2(0f, 0.88f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f, 0f);
            rect.sizeDelta = Vector2.zero;

            // Kept out of any layout group on the button, and as wide as it is high.
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var fitter = go.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fitter.aspectRatio = 1f;

            image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;

            // Room for the icon at the left of the text.
            var label = entry.Button.TextInstance;
            if (label)
            {
                var height = button.GetComponent<RectTransform>().rect.height;
                var room = height > 1f ? 16f + height * 0.76f : 44f;
                var margin = label!.margin;
                label.margin = new Vector4(Mathf.Max(margin.x, room), margin.y, margin.z, margin.w);
            }
        }

        image!.sprite = sprite;
        if (!image.gameObject.activeSelf)
            image.gameObject.SetActive(true);
    }

    // The cheat button area's layout is in Starlight's prefab, so it is logged once per open (after the
    // layout has run) for a tester's log.
    private static void LogLayout(int generation)
    {
        if (layoutLoggedGeneration == generation)
            return;

        var first = Entries.FirstOrDefault(e => e.Button.ButtonInstance);
        var button = first?.Button.ButtonInstance;
        if (!button)
            return;

        layoutLoggedGeneration = generation;
        var content = button!.transform.parent;
        var rect = button.GetComponent<RectTransform>().rect;
        var label = first!.Button.TextInstance;
        var shown = Entries.Count(e => e.Button.ButtonInstance && e.Button.ButtonInstance.gameObject.activeSelf);
        var icons = Entries.Count(e => e.Weather && e.Button.ButtonInstance && e.Button.ButtonInstance.transform.Find(IconName));
        SrLogger.LogMessage($"Cheat menu layout: {(content ? content!.name : "no parent")} " +
                            $"grid={(content && content!.GetComponent<GridLayoutGroup>())} " +
                            $"vertical={(content && content!.GetComponent<VerticalLayoutGroup>())} " +
                            $"children={(content ? content!.childCount : 0)}, ours shown={shown}, weather icons={icons}, " +
                            $"zone {(string.IsNullOrEmpty(weatherZone) ? "none" : weatherZone)} with {weatherTypes.Count} weather types, " +
                            $"button {rect.width:0}x{rect.height:0}, label {(label ? label!.alignment.ToString() : "none")}");
    }
}
