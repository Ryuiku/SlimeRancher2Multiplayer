using HarmonyLib;
using Il2CppMonomiPark.World;
using SR2MP.Packets.World;

namespace SR2MP.Patches.World;

[HarmonyPatch(typeof(AccessDoor), nameof(AccessDoor.CurrState),  MethodType.Setter)]
internal static class OnAccessDoorUnlock
{
    public static void Postfix(AccessDoor __instance, AccessDoor.State value)
    {
        if (HandlingPacket) return;

        // Not before the host's world has arrived: until then these are the joiner's own doors.
        if (!SR2MP.Shared.Managers.NetworkActorManager.CanSendActorChanges) return;

        var packet = new AccessDoorPacket
        {
            ID = __instance._id,
            State = value,
        };
        Main.SendToAllOrServer(packet);
    }
}