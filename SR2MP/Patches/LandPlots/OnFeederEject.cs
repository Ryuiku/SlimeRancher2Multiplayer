using HarmonyLib;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.LandPlots;

// Runs after EjectFood, AmmoSlot.DecrementAmmo and the remainingFeedOperations step, both live
// (FixedUpdate) and during catch-up (ProcessFeedOperationFastForward jumps straight into
// ProcessFeedOperation(false)). Only the game that runs the ranch area calls it, so the schedule
// it leaves behind is the one the other players and the host's save should have.
[HarmonyPatch(typeof(SlimeFeeder), nameof(SlimeFeeder.ProcessFeedOperation))]
internal static class OnFeederProcessFeedOperation
{
    public static void Postfix(SlimeFeeder __instance) => NetworkFeederSync.MarkDirty(__instance);
}

// Catch-up (the FeedingSource.AutoFeeder constructor) counts the feedings that came due since
// nextFeedingTime and moves the schedule forward without ejecting anything.
[HarmonyPatch(typeof(SlimeFeeder), nameof(SlimeFeeder.UpdateToTime))]
internal static class OnFeederUpdateToTime
{
    public static void Postfix(SlimeFeeder __instance) => NetworkFeederSync.MarkDirty(__instance);
}
