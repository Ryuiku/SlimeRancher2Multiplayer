using HarmonyLib;
using SR2MP.Components.Actor;

namespace SR2MP.Patches.Actor;

// Ringtails turn to stone in daylight: the game removes the slime and spawns a separate statue
// actor, and turns it back at night. Every player's game ran this on its own copy of the ringtail,
// so each made its own statue and two statues sat in the same spot. The vac then pulled both, and
// the one it held got pushed out of the nozzle straight away. Only the player who owns the actor
// does the swap now; everyone else gets the result through the usual spawn and destroy packets.
internal static class RingtailGate
{
    internal static bool MayRun(Component component)
    {
        if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected))
            return true;

        var networkActor = component.GetComponentInParent<NetworkActor>();
        return !networkActor || networkActor.LocallyOwned;
    }
}

[HarmonyPatch(typeof(TurnToStoneInDaylight), nameof(TurnToStoneInDaylight.BecomeStatue))]
internal static class OnBecomeStatue
{
    public static bool Prefix(TurnToStoneInDaylight __instance) => RingtailGate.MayRun(__instance);
}

// A statue that another player's game created arrived here without the link to its slime
// (referencedActorId 0), and those statues were saved that way. The game then failed to turn them
// back every frame in the shade (KeyNotFoundException for key 0 in Player.log) and dropped them
// out of the vac. Such a statue is replaced with a fresh ringtail instead.
[HarmonyPatch(typeof(RingtailStatue), nameof(RingtailStatue.Unstatue))]
internal static class OnUnstatue
{
    private static IdentifiableType? ringtail;

    public static bool Prefix(RingtailStatue __instance)
    {
        if (!RingtailGate.MayRun(__instance))
            return false;

        try
        {
            var model = __instance._model;
            if (model == null || model.referencedActorId.Value != 0)
                return true;

            ReplaceWithRingtail(__instance, model);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not repair a ringtail statue: {ex.Message}");
        }

        // Never let the game run its own version for these: it throws every frame.
        return false;
    }

    private static void ReplaceWithRingtail(RingtailStatue statue, Il2CppMonomiPark.SlimeRancher.DataModel.StatueFormModel model)
    {
        ringtail ??= ActorManager.ActorTypes.Values.FirstOrDefault(t =>
            t && string.Equals(t.name, "Ringtail", StringComparison.OrdinalIgnoreCase));

        if (!ringtail || !ringtail!.prefab)
        {
            SrLogger.LogWarning("Could not repair a ringtail statue: the Ringtail slime type was not found.");
            return;
        }

        var transform = statue.transform;
        var slime = InstantiationHelpers.InstantiateActor(
            ringtail.prefab, model.sceneGroup, transform.position, transform.rotation, false,
            SlimeAppearance.AppearanceSaveSet.NONE, SlimeAppearance.AppearanceSaveSet.NONE,
            new Il2CppSystem.Nullable<Il2CppMonomiPark.SlimeRancher.Player.AmmoSlot.AmmoMetadata>(), false, false);

        if (!slime)
            return;

        Destroyer.DestroyActor(statue.gameObject, "SR2MP.RepairRingtailStatue", false);
        SrLogger.LogMessage($"Replaced a ringtail statue that had lost its slime (actor {model.actorId.Value}) with a ringtail.");
    }
}
