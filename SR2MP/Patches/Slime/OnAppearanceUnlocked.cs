using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;
using SR2MP.Shared.Utils;

namespace SR2MP.Patches.Slime;

// Slime appearances unlocked from any source (pod, Labyrinth reward, kiosk, shop) count for every
// player. Only the pod route was shared before. Which appearance is selected stays each player's own.
[HarmonyPatch(typeof(SlimeAppearanceDirector), nameof(SlimeAppearanceDirector.UnlockAppearance))]
internal static class OnAppearanceUnlocked
{
    public static void Postfix(SlimeDefinition slimeDefinition, SlimeAppearance appearance)
    {
        if (!slimeDefinition || !appearance || !UnlockLookup.CanSend)
            return;

        try
        {
            Main.SendToAllOrServer(new UnlockSharedPacket
            {
                Kind = UnlockSharedPacket.UnlockKind.Appearance,
                IdentId = NetworkActorManager.GetPersistentID(slimeDefinition),
                SaveSet = (byte)appearance.SaveSet
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a slime appearance: {ex.Message}");
        }
    }
}

// A secret style found by one player is found for everyone.
[HarmonyPatch(typeof(SecretStyleDiscoveriesModel), nameof(SecretStyleDiscoveriesModel.Discover))]
internal static class OnSecretStyleDiscovered
{
    public static void Postfix(SlimeDefinition slimeDefinition, bool __result)
    {
        if (!__result || !slimeDefinition || !UnlockLookup.CanSend)
            return;

        try
        {
            Main.SendToAllOrServer(new UnlockSharedPacket
            {
                Kind = UnlockSharedPacket.UnlockKind.SecretStyle,
                IdentId = NetworkActorManager.GetPersistentID(slimeDefinition)
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a secret style: {ex.Message}");
        }
    }
}
