using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Packets.Switch;

namespace SR2MP.Patches.Switches;

[HarmonyPatch]
internal static class OnSwitchActivated
{
    [HarmonyPostfix, HarmonyPatch(typeof(WorldStatePrimarySwitch), nameof(WorldStatePrimarySwitch.SetStateForAll))]
    public static void SetPrimary(WorldStatePrimarySwitch __instance, SwitchHandler.State state, bool immediate)
    {
        SendPacket(state, __instance.SwitchDefinition.ID, immediate);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(WorldStateSecondarySwitch), nameof(WorldStateSecondarySwitch.SetState))]
    public static void SetSecondary(WorldStateSecondarySwitch __instance, SwitchHandler.State state, bool immediate)
    {
        SendPacket(state, __instance._primary.SwitchDefinition.ID, immediate);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(WorldStateInvisibleSwitch), nameof(WorldStateInvisibleSwitch.SetStateForAll))]
    public static void SetInvisible(WorldStateInvisibleSwitch __instance, SwitchHandler.State state, bool immediate)
    {
        SendPacket(state, __instance.SwitchDefinition.ID, immediate);
    }

    private static void SendPacket(SwitchHandler.State state, string id, bool immediate)
    {
        if (HandlingPacket) return;

        // Not before the host's world has arrived: until then these are the joiner's own switches.
        if (!SR2MP.Shared.Managers.NetworkActorManager.CanSendActorChanges) return;

        Main.SendToAllOrServer(new WorldSwitchPacket
        {
            ID = id,
            State = state,
            Immediate = immediate
        });
    }
}