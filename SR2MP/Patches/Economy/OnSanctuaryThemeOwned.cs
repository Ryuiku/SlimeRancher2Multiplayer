using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.UI.Themes;
using SR2MP.Packets.Switch;
using SR2MP.Packets.World;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.Economy;

// Sanctuary themes are owned per player in the game; here a bought theme counts for everyone.
[HarmonyPatch(typeof(SanctuaryThemeInventoryModel))]
internal static class OnSanctuaryThemeOwned
{
    [HarmonyPostfix, HarmonyPatch(nameof(SanctuaryThemeInventoryModel.SetThemeOwned))]
    public static void Owned(ThemeDefinition themeDefinition) => Send(themeDefinition, true);

    // Only sent if the game ever takes a theme back, so that matches for everyone too.
    [HarmonyPostfix, HarmonyPatch(nameof(SanctuaryThemeInventoryModel.SetThemeUnowned))]
    public static void Unowned(ThemeDefinition themeDefinition) => Send(themeDefinition, false);

    private static void Send(ThemeDefinition theme, bool owned)
    {
        if (!theme || !UnlockLookup.CanSend)
            return;

        try
        {
            var id = theme.ReferenceId;
            if (string.IsNullOrEmpty(id))
                return;

            Main.SendToAllOrServer(new UnlockSharedPacket
            {
                Kind = UnlockSharedPacket.UnlockKind.Theme,
                RefId = id,
                Owned = owned
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a sanctuary theme: {ex.Message}");
        }
    }
}

// A bought sanctuary room is a world switch. The switch patch shares it when the purchase goes
// through a switch component; this covers a purchase that writes the switch model directly.
// The switch handler ignores a state it already has, so getting both is harmless.
[HarmonyPatch(typeof(AcquisitionUtility), nameof(AcquisitionUtility.AcquireSanctuaryRoom))]
internal static class OnSanctuaryRoomAcquired
{
    public static void Postfix(SanctuaryRoomDefinition room, bool __result)
    {
        if (!__result || !room || !UnlockLookup.CanSend)
            return;

        try
        {
            var definition = room.WorldSwitch;
            if (!definition || string.IsNullOrEmpty(definition.ID))
                return;

            var state = SceneContext.Instance.GameModel.GetSwitchState(definition);

            // Still in its starting state: the purchase changes it later, and the switch patch
            // sends it then.
            if (state == definition.InitState)
            {
                SrLogger.LogDebug($"Sanctuary room {definition.ID} bought, its switch has not changed yet ({state})");
                return;
            }

            Main.SendToAllOrServer(new WorldSwitchPacket
            {
                ID = definition.ID,
                State = state,
                Immediate = true
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a sanctuary room: {ex.Message}");
        }
    }
}
