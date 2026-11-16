using HarmonyLib;
using SR2MP.Components.Actor;

namespace SR2MP.Patches.Slime;

// Only the player simulating a slime lets it eat. Every player's game ran the eating logic for every
// slime, so food and plorts got eaten (and deleted for everyone) by copies of slimes on other players'
// machines, and largo transformations ran more than once.
[HarmonyPatch(typeof(SlimeEat))]
internal static class OnSlimeChomp
{
    private static bool MayEat(SlimeEat slimeEat)
    {
        if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected))
            return true;

        var networkActor = slimeEat.GetComponent<NetworkActor>();
        return !networkActor || networkActor.LocallyOwned;
    }

    [HarmonyPatch(nameof(SlimeEat.MaybeChomp)), HarmonyPrefix]
    public static bool MaybeChompPrefix(SlimeEat __instance, ref bool __result)
    {
        if (MayEat(__instance))
            return true;

        __result = false;
        return false;
    }

    [HarmonyPatch(nameof(SlimeEat.MaybeSpinAndChomp)), HarmonyPrefix]
    public static bool MaybeSpinAndChompPrefix(SlimeEat __instance, ref bool __result)
    {
        if (MayEat(__instance))
            return true;

        __result = false;
        return false;
    }
}
