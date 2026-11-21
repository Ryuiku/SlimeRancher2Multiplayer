using HarmonyLib;
using SR2MP.Packets.Geyser;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.Geyser;

// Every game runs each geyser's random timer on its own, and every game also replays the others'
// eruptions, so a geyser erupted several times per cycle and out of step. Of the players near a
// geyser, one runs it (WorldAuthority) and the others only play what it sends.
internal static class GeyserAuthority
{
    internal const float Radius = 150f;

    internal static bool RunsHere(Il2Cpp.Geyser geyser)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return true;
        if (HandlingPacket) return true;
        if (!geyser) return true;

        return WorldAuthority.IsLocalAuthority(geyser.transform.position, Radius);
    }
}

[HarmonyPatch(typeof(Il2Cpp.Geyser), nameof(Il2Cpp.Geyser.TriggerGeyser))]
internal static class OnGeyserTrigger
{
    public static bool Prefix(Il2Cpp.Geyser __instance)
    {
        try
        {
            return GeyserAuthority.RunsHere(__instance);
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not check geyser trigger: {ex.Message}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(Il2Cpp.Geyser._RunGeyser_d__30), nameof(Il2Cpp.Geyser._RunGeyser_d__30.MoveNext))]
internal static class OnGeyserFire
{
    public static bool Prefix(Il2Cpp.Geyser._RunGeyser_d__30 __instance, ref bool __result)
    {
        if (__instance.__1__state != 0)
            return true;

        if (HandlingPacket)
            return true;

        try
        {
            // Started here by the geyser's own timer, but another player runs this geyser: end the
            // eruption before it does anything; theirs arrives as a packet.
            if (!GeyserAuthority.RunsHere(__instance.__4__this))
            {
                __result = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not check geyser eruption: {ex.Message}");
        }

        var packet = new GeyserTriggerPacket
        {
            ObjectPath = __instance.__4__this.gameObject.GetGameObjectPath(),
            Duration = __instance.duration
        };

        Main.SendToAllOrServer(packet);
        return true;
    }
}
