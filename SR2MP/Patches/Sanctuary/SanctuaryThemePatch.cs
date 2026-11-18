using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.UI.Themes;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Sanctuary;

// A room's theme is picked through the room; the game itself also sets it while the room loads.
// Only a real change, made outside a zone load and outside an applied packet, is sent.
[HarmonyPatch(typeof(SanctuaryRoomController))]
internal static class SanctuaryRoomThemePatch
{
    [HarmonyPrefix, HarmonyPatch(nameof(SanctuaryRoomController.SetTheme))]
    public static void SetThemePrefix(SanctuaryRoomController __instance, out string? __state)
    {
        __state = null;
        try
        {
            __state = __instance.GetTheme()?.ReferenceId;
        }
        catch
        {
            // a room without a model yet
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(SanctuaryRoomController.SetTheme))]
    public static void SetThemePostfix(SanctuaryRoomController __instance, ThemeDefinition theme, string? __state)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        try
        {
            if (!theme)
                return;

            NetworkSanctuaryManager.ReportThemeChange(
                NetworkSanctuaryManager.ResolveRoomId(__instance), __state, theme.ReferenceId);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a sanctuary room theme: {ex.Message}");
        }
    }

    // Runs inside the zone load: only remembers the room and fills in what arrived for it
    // before it had a model. Nothing is sent from here.
    [HarmonyPrefix, HarmonyPatch(nameof(SanctuaryRoomController.SetModel))]
    public static void SetModelPrefix(SanctuaryRoomController __instance, SanctuaryRoomModel model)
    {
        try
        {
            if (model == null)
                return;

            // Also outside a session: a player may load the Sanctuary first and host or join after.
            NetworkSanctuaryManager.RegisterRoom(__instance, model);
            NetworkSanctuaryManager.ApplyPendingTo(__instance, model);
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            SrLogger.LogWarning($"Could not prepare a sanctuary room: {ex.Message}");
        }
    }
}

// Pre-placed gadgets written to a room's model.
[HarmonyPatch(typeof(SanctuaryRoomModel))]
internal static class SanctuaryRoomModelPatch
{
    // No patch on SanctuaryRoomModel.SetTheme: its code is a plain field setter shared with 32
    // other methods, so a patch there would run for all of them. Theme changes are sent from
    // SanctuaryRoomController.SetTheme above.

    [HarmonyPostfix, HarmonyPatch(nameof(SanctuaryRoomModel.SetPreplacedGadget))]
    public static void SetPreplacedGadgetPostfix(SanctuaryRoomModel __instance,
        SanctuaryRoomPreplacedGadgetId id, ActorId gadgetActorId)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected)
            return;

        try
        {
            NetworkSanctuaryManager.ReportPreplacedGadget(__instance, id?.Value, gadgetActorId.Value);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a sanctuary pre-placed gadget: {ex.Message}");
        }
    }
}
