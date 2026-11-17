using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player;
using SR2MP.Packets.Ammo;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Ammo;

[HarmonyPatch(typeof(AmmoSlotManager), nameof(AmmoSlotManager.MaybeAddToAnySlot),
    typeof(AmmoSlot.AmmoMetadata),
    typeof(bool))]
internal static class OnAmmoAddToAny
{
    // Runs inside game code, including zone loads (sprinkle canisters catch up while the Sanctuary
    // loads). An exception here would break what the game was doing, so none may leave.
    public static void Postfix(AmmoSlotManager __instance, ref bool __result, AmmoSlot.AmmoMetadata metadata)
    {
        if ((!Main.Client.IsConnected && !Main.Server.IsRunning) || HandlingPacket) return;

        if (OnSiloStorageAddResource.isDroneResource) return;

        if (!__result)
            return;

        try
        {
            // Most storages (the player's own vac among them) are not shared: check that first.
            var id = __instance.GetPlotID();
            if (id == null || metadata == null || !metadata.Id) return;

            NetworkSprinkleCanisterManager.MarkDirtyByAmmoKey(id);

            Main.SendToAllOrServer(new AmmoAddPacket
            {
                Identifiable = NetworkActorManager.GetPersistentID(metadata.Id),
                Count = 1,
                ID = id,
            });
        }
        catch (Exception ex)
        {
            AmmoPatchErrors.Report(nameof(OnAmmoAddToAny), ex);
        }
    }
}

/// <summary>
/// Logs an exception from an ammo patch once per patch, and keeps a line in the load trace.
/// </summary>
internal static class AmmoPatchErrors
{
    private static readonly HashSet<string> Reported = new();

    internal static void Report(string patch, Exception ex)
    {
        SR2MP.Shared.Utils.SceneLoadTrace.Record($"{patch} threw: {ex.Message}");

        if (Reported.Add(patch))
            SrLogger.LogWarning($"{patch} failed (reported once): {ex}");
    }
}
