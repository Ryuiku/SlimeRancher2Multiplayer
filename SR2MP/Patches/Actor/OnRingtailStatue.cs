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

[HarmonyPatch(typeof(RingtailStatue), nameof(RingtailStatue.Unstatue))]
internal static class OnUnstatue
{
    public static bool Prefix(RingtailStatue __instance) => RingtailGate.MayRun(__instance);
}
