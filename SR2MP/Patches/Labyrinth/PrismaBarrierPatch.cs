using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Labyrinth;
using SR2MP.Packets.World;

namespace SR2MP.Patches.Labyrinth;

[HarmonyPatch(typeof(PrismaBarrier), nameof(PrismaBarrier.SetActivationTime))]
internal static class OnPrismaBarrierActivate
{
    public static void Postfix(PrismaBarrier __instance, double activationTime)
    {
        if (HandlingPacket) return;

        // Only the host sends. Every game runs its barriers from the same disruption forecast (the
        // host's, see NetworkPrismaManager), so the host's packet only lines up the exact times.
        // When every machine sent its own, two players in the Labyrinth kept overwriting each other.
        if (!Main.Server.IsRunning) return;

        var id = "";
        foreach (var pair in GameState.AllPrismaBarriers())
        {
            if (pair.value == __instance._model)
            {
                id = pair.key;
                break;
            }
        }

        if (!string.IsNullOrEmpty(id))
        {
            Main.Server.SendToAll(new PrismaBarrierPacket
            {
                ID = id,
                ActivationTime = activationTime
            });
        }
    }
}