using HarmonyLib;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Time;

[HarmonyPatch(typeof(TimeDirector), nameof(TimeDirector.FastForwardTo))]
internal static class OnFastForward
{
    /// <summary>
    /// Where the time skip that is running on this machine ends, as far as this mod has seen.
    /// </summary>
    internal static double RunningTarget;

    // A skip that is running is never shortened. Two players can ask for different wake-up times
    // (one sleeps, another is knocked out); if the later request replaced the earlier, longer one,
    // the first player stayed held in place after the skip stopped, and clocks drifted apart.
    public static void Prefix(TimeDirector __instance, [HarmonyArgument(0)] ref double fastForwardUntil)
    {
        try
        {
            if ((Main.Server.IsRunning || Main.Client.IsConnected)
                && __instance.IsFastForwarding()
                && RunningTarget > fastForwardUntil
                && RunningTarget > __instance._worldModel.worldTime)
            {
                fastForwardUntil = RunningTarget;
            }
        }
        catch
        {
            // keep the game's own target
        }

        RunningTarget = fastForwardUntil;
    }

    public static void Postfix([HarmonyArgument(0)] double fastForwardUntil)
    {
        NetworkGardenManager.RestoreAfterTimeSkip();

        if (HandlingPacket)
            return;

        // A joining player's clock is still the one from their own save.
        if (!NetworkActorManager.CanSendActorChanges)
            return;

        var packet = new FastForwardPacket
        {
            Time = fastForwardUntil
        };

        Main.SendToAllOrServer(packet);
    }
}