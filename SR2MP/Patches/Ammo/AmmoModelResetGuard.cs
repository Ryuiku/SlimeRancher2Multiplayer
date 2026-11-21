using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Player;

namespace SR2MP.Patches.Ammo;

/// <summary>
/// A storage's saved contents with more slots than the storage has made the game throw while it
/// built the plot (AmmoModel.Reset reads one slot definition per saved slot), and a throw while a
/// zone loads sends the player back to the main menu. Hosts were thrown out entering the Sanctuary
/// this way. The extra slots are cut off before the game reads them, and what was in them is
/// logged. Only cases that would have thrown are changed.
/// AmmoModel.Reset has its own native code (no other method shares it), so patching it is safe.
/// </summary>
[HarmonyPatch(typeof(AmmoModel), nameof(AmmoModel.Reset))]
internal static class AmmoModelResetGuard
{
    // Set by the SiloStorage.SetModel prefix below, so the log names the plot.
    [ThreadStatic] private static string? currentStorage;

    public static void Prefix(AmmoModel __instance, Il2CppReferenceArray<AmmoSlotDefinition> __0)
    {
        try
        {
            var slots = __instance?.Slots;
            if (slots == null || __0 == null)
                return;

            var wanted = __0.Length;
            if (slots.Length <= wanted)
                return;

            var dropped = new List<string>();
            for (var i = wanted; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot != null && slot._count > 0)
                    dropped.Add($"{slot._count} x {(slot._id ? slot._id.name : "?")}");
            }

            var trimmed = new Il2CppReferenceArray<AmmoSlot>(wanted);
            for (var i = 0; i < wanted; i++)
                trimmed[i] = slots[i];

            __instance!.Slots = trimmed;

            SrLogger.LogWarning(
                $"Storage {currentStorage ?? "?"} had {slots.Length} saved slots but only {wanted} exist; " +
                $"cut it down so the area loads. Lost from the extra slots: {(dropped.Count == 0 ? "nothing" : string.Join(", ", dropped))}");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not check a storage's saved slots: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(SiloStorage), nameof(SiloStorage.SetModel))]
    internal static class NameStorage
    {
        public static void Prefix(SiloStorage __instance)
        {
            try
            {
                var plot = __instance ? __instance.GetComponentInParent<LandPlotLocation>(true) : null;
                var set = __instance && __instance.AmmoSetReference ? __instance.AmmoSetReference.name : "?";
                currentStorage = $"{(plot ? plot._id : "?")}/{set}";
            }
            catch
            {
                currentStorage = null;
            }
        }

        public static void Postfix() => currentStorage = null;
    }
}
