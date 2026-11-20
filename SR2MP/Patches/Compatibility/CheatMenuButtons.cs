using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Starlight.Buttons;
using Starlight.Enums.Sounds;
using Starlight.Menus;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Compatibility;

/// <summary>
/// Extra buttons in Starlight's cheat menu: the ranch house rests, weather, and things that are
/// slow or random to make happen when testing (tangle bud stalks, garden growth, sloomber yawns,
/// dervish cyclones, gordo bursts, tornados). They work alone and in multiplayer, where the host's
/// cheat setting decides, and they go through the game's own code so the sync carries them.
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
    }

    private static readonly List<Entry> Entries = new();
    private static bool registered;

    private static List<NetworkCheats.WeatherChoice> weatherChoices = new();
    private static string weatherChoiceZone = string.Empty;
    private static string chosenWeather = string.Empty;

    private static string ChosenLabel => string.IsNullOrEmpty(chosenWeather) ? "none" : chosenWeather;

    private static StarlightCheatMenu? Menu => MenuEUtil.GetMenu<StarlightCheatMenu>();

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

            RefreshWeatherChoices();
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
                Refresh(entry);
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

        Add(() => $"Weather here now: {NetworkCheats.RunningWeatherHere()} (click to refresh)", RefreshAllLabels, needsCheats: false);
        Add(() => $"Weather to pick: {ChosenLabel} (click to change)", NextWeatherChoice, needsCheats: false);
        Add(() => $"Add weather here: {ChosenLabel}", () => ApplyWeather(NetworkCheats.WeatherMode.Add));
        Add(() => $"Set weather here (replaces): {ChosenLabel}", () => ApplyWeather(NetworkCheats.WeatherMode.Set));
        Add(() => "Clear weather here", () => ApplyWeather(NetworkCheats.WeatherMode.Clear));
        Add(() => "Spawn a tornado ahead", NetworkCheats.TornadoHere);
        Add(() => "Grow a tangle bud stalk where I look", NetworkCheats.GrowTangleStalk);
        Add(() => "Grow and ripen ranch gardens now", () => NetworkCheats.GrowRanchGardens(out _));
        Add(() => "Make nearby sloombers yawn", NetworkCheats.SloombersYawn);
        Add(() => "Make nearby dervishes spin a cyclone", NetworkCheats.DervishesSpin);
        Add(() => "Burst the gordo I look at", NetworkCheats.BurstGordo);
    }

    private static void Add(Func<string> label, Func<string> action, bool closeFirst = false, bool needsCheats = true)
    {
        var entry = new Entry { Label = label };
        entry.Button = new StarlightCheatMenuButton(label(), () => Press(entry, action, closeFirst, needsCheats));

        // Starlight logs and leaves out a button whose label another button already has.
        if (StarlightButtons()?.Contains(entry.Button) ?? false)
            Entries.Add(entry);
        else
            SrLogger.LogWarning($"Cheat menu: button '{entry.Button.Label}' was not added");
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

    private static void Refresh(Entry entry)
    {
        var text = entry.Label();
        entry.Button.Label = text;

        if (!string.IsNullOrEmpty(entry.Result))
            text = $"{text}: {entry.Result}";

        var menu = Menu;
        var label = entry.Button.TextInstance;
        if (menu && menu!.isOpen && label)
            label!.text = text;
    }

    // ---------------------------------------------------------------- weather choice

    private static string RefreshAllLabels()
    {
        foreach (var entry in Entries)
            Refresh(entry);

        return string.Empty;
    }

    private static void RefreshWeatherChoices()
    {
        weatherChoices = NetworkCheats.WeatherChoicesHere(out weatherChoiceZone);
        if (weatherChoices.Count == 0)
            chosenWeather = string.Empty;
        else if (weatherChoices.All(choice => choice.Label != chosenWeather))
            chosenWeather = weatherChoices[0].Label;
    }

    private static string NextWeatherChoice()
    {
        RefreshWeatherChoices();
        if (weatherChoices.Count > 0)
        {
            var index = weatherChoices.FindIndex(choice => choice.Label == chosenWeather);
            chosenWeather = weatherChoices[(index + 1) % weatherChoices.Count].Label;
        }

        // The add and set buttons show the choice too.
        RefreshAllLabels();

        if (string.IsNullOrEmpty(weatherChoiceZone))
            return "no weather here";

        return weatherChoices.Count switch
        {
            0 => "no weather in this zone",
            1 => "only one weather here",
            _ => string.Empty,
        };
    }

    private static string ApplyWeather(NetworkCheats.WeatherMode mode)
    {
        WeatherStateDefinition? state = null;
        if (mode != NetworkCheats.WeatherMode.Clear)
        {
            RefreshWeatherChoices();
            var choice = weatherChoices.FirstOrDefault(c => c.Label == chosenWeather);
            if (choice == null)
                return "no weather to pick here";

            state = choice.State;
        }

        var result = NetworkCheats.ApplyWeatherHere(state, mode);

        // The registry applies it on its next update (and a client hears it from the host), so
        // refresh "Weather here now" a little later.
        NetworkCheats.Later(() => RefreshAllLabels(), 40);
        return result;
    }
}
