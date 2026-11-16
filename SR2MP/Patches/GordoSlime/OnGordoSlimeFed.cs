using HarmonyLib;
using SR2MP.Packets.FX;
using SR2MP.Packets.GordoSlime;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.GordoSlime;

[HarmonyPatch(typeof(GordoEat), nameof(GordoEat.DoEat))]
internal static class OnGordoSlimeFed
{
    // Every machine that has the gordo loaded has its own copy of the food, and each copy used to
    // feed the gordo and report the new count. Only the machine that simulates the food feeds it.
    public static bool Prefix(GameObject obj, out bool __state)
    {
        __state = true;

        if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected))
            return true;

        var networkActor = obj ? obj.GetComponent<SR2MP.Components.Actor.NetworkActor>() : null;
        if (networkActor != null && !networkActor.LocallyOwned)
        {
            __state = false;
            return false;
        }

        return true;
    }

    public static void Postfix(GordoEat __instance, bool __state)
    {
        if (!__state)
            return;

        var packet = new GordoSlimeFeedPacket
        {
            ID = __instance.Id,
            NewFoodCount = __instance.GordoModel.GordoEatenCount,
            RequiredFoodCount = __instance.GordoModel.targetCount,
            GordoSlimeType = NetworkActorManager.GetPersistentID(__instance.GordoModel.identifiableType)
        };
        Main.SendToAllOrServer(packet);

        var soundPacket = new WorldFXPacket
        {
            Position = __instance.transform.position,
            FX = WorldFXType.GordoFoodEatenSound
        };
        Main.SendToAllOrServer(soundPacket);
    }
}