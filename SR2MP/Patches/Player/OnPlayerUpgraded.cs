using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Packets.Upgrade;

namespace SR2MP.Patches.Player;

[HarmonyPatch(typeof(UpgradeModel), nameof(UpgradeModel.IncrementUpgradeLevel))]
internal static class OnPlayerUpgraded
{
    public static void Prefix(UpgradeModel __instance, [HarmonyArgument(0)] UpgradeDefinition definition, out int __state)
    {
        __state = -1;

        try
        {
            if (definition != null)
                __state = __instance.GetUpgradeLevel(definition);
        }
        catch
        {
            // ignored
        }
    }

    public static void Postfix(UpgradeModel __instance, [HarmonyArgument(0)] UpgradeDefinition definition, int __state)
    {
        if (HandlingPacket) return;

        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        if (definition == null) return;

        // Nothing changed (already at the top level).
        var level = __instance.GetUpgradeLevel(definition);
        if (__state >= 0 && level <= __state) return;

        var packet = new PlayerUpgradePacket
        {
            UpgradeID = (byte)definition._uniqueId,
            Level = (byte)Math.Clamp(level, 0, 255)
        };

        Main.SendToAllOrServer(packet);
    }
}