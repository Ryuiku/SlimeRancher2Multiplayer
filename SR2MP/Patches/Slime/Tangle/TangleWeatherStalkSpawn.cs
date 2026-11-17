using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;

namespace SR2MP.Patches.Slime.Tangle;

// Tangle weather plants stalks that grow a bud actor. Every player's weather planted its own, so
// buds were doubled. Like tornadoes, only the host's weather plants them; the host's buds are
// shared with everyone as normal actors.
[HarmonyPatch(typeof(SpawnTangleStalkActivity), nameof(SpawnTangleStalkActivity.Spawn))]
internal static class TangleWeatherStalkSpawn
{
    public static bool Prefix(ref GameObject __result)
    {
        if (Main.Client.IsConnected && !Main.Server.IsRunning && !HandlingPacket)
        {
            // The weather code accepts nothing spawned, as it does for the tornado patch.
            __result = null!;
            return false;
        }

        return true;
    }
}
