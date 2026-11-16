using Il2CppMonomiPark.SlimeRancher.DataModel;

namespace SR2MP.Shared.Managers;

internal static class NetworkGordoSlimeManager
{
    private static readonly HashSet<string> PoppedGordoSlimeIds = new();
    private static readonly HashSet<string> RewardsGivenIds = new();

    // The value the game itself stores as a gordo's eaten count once it has burst (GordoModel.HasPopped).
    internal const int PoppedEatenCount = -1;

    internal static void MarkPopped(string id) => PoppedGordoSlimeIds.Add(id);

    // These sets describe one world. Without clearing them, gordos popped in one session stayed hidden
    // and gave no rewards in the next world joined or hosted in the same game run.
    internal static void Reset()
    {
        PoppedGordoSlimeIds.Clear();
        RewardsGivenIds.Clear();
    }

    internal static bool HasGivenRewards(string id) => RewardsGivenIds.Contains(id);

    internal static void MarkRewarded(string id) => RewardsGivenIds.Add(id);

    internal static void ApplyState(string id, GordoModel model)
    {
        if (!model.gameObj) return;

        var gordoSlimeComponent = model.gameObj.GetComponent<GordoEat>();
        if (!gordoSlimeComponent) return;

        gordoSlimeComponent.SetModel(model);

        if (PoppedGordoSlimeIds.Contains(id))
            model.gameObj.SetActive(false);
    }
    
    internal static void ReapplyOnSpawn(GordoEat gordoSlime)
    {
        if (!Main.Server.IsRunning && !Main.Client.IsConnected) return;

        if (PoppedGordoSlimeIds.Contains(gordoSlime.Id))
            gordoSlime.gameObject.SetActive(false);
    }
    
    internal static string? ResolveGordoSlimeId(GordoModel model)
    {
        if (model.gameObj)
        {
            var gordoSlime = model.gameObj.GetComponent<GordoEat>();
            if (gordoSlime != null && !string.IsNullOrEmpty(gordoSlime.Id))
                return gordoSlime.Id;
        }

        foreach (var pair in GameState.gordos)
        {
            if (pair.value.Equals(model))
                return pair.key;
        }

        return null;
    }
}
