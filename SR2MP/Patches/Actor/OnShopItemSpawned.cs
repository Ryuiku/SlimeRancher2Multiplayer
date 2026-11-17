using System.Collections;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Shop;
using SR2MP.Components.Actor;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Actor;

// Items bought in a shop (toys) come out of the kiosk through ShopDirector.SpawnNextItem. If the
// game builds them without InstantiateActor, OnActorSpawn never sees them and the toy only exists
// for the player who bought it. Announcing it here covers that path; when InstantiateActor did run,
// the toy already has a NetworkActor and nothing more happens.
[HarmonyPatch(typeof(ShopDirector), nameof(ShopDirector.SpawnNextItem))]
internal static class OnShopItemSpawned
{
    public static void Postfix(GameObject __result)
    {
        try
        {
            if (HandlingPacket) return;
            if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;
            if (!__result) return;

            StartCoroutine(AnnounceNextFrame(__result));
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not announce a shop item: {ex.Message}");
        }
    }

    // One frame later, like OnActorSpawn: the actor's model can be set in Awake/Start, and by then
    // OnActorSpawn has also had its chance to announce it.
    private static IEnumerator AnnounceNextFrame(GameObject item)
    {
        yield return null;

        if (!item) yield break;
        if (!NetworkActorManager.CanSendActorChanges) yield break;

        if (item.GetComponent<NetworkActor>())
        {
            SrLogger.LogDebug($"Shop item {item.name} was already announced as a normal actor spawn");
            yield break;
        }

        try
        {
            ActorManager.RegisterSpawnOverNetwork(item);

            if (item.GetComponent<NetworkActor>())
                SrLogger.LogDebug($"Shop item {item.name} announced from the shop spawn");
            else
                SrLogger.LogDebug($"Shop item {item.name} is not an actor with a model; nothing announced");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not announce shop item {item.name}: {ex.Message}");
        }
    }
}
