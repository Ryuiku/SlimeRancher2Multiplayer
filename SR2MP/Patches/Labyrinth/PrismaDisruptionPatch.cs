using System.Reflection;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Labyrinth;
using SR2MP.Packets.World;
using SR2MP.Shared.Managers;

namespace SR2MP.Patches.Labyrinth;

// The disruption forecast is rolled at random. Only the host rolls it (and culls the future
// part of it); a client keeps the forecast the host sent (Shared/Managers/NetworkPrismaManager.cs).
// CullPassedForecasts and the area refreshes are left alone: they only tidy what is already there.
[HarmonyPatch]
internal static class PrismaDisruptionForecastPatch
{
    private static readonly string[] Methods =
    {
        nameof(PrismaDisruptionModel.ForecastAll),
        nameof(PrismaDisruptionModel.ForecastGroup),
        nameof(PrismaDisruptionModel.ForecastGroups),
        nameof(PrismaDisruptionModel.PopulateTimedForecasts),
        nameof(PrismaDisruptionModel.PopulateInitialForecasts),
        nameof(PrismaDisruptionModel.CullFutureForecasts)
    };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in Methods)
        {
            var method = AccessTools.Method(typeof(PrismaDisruptionModel), name);
            if (method != null)
                yield return method;
            else
                SrLogger.LogWarning($"Prisma forecast: PrismaDisruptionModel.{name} not found, not patched.");
        }
    }

    private static bool IsClientOnly => Main.Client.IsConnected && !Main.Server.IsRunning;

    public static bool Prefix() => !IsClientOnly;

    public static void Postfix()
    {
        if (Main.Server.IsRunning)
            NetworkPrismaManager.MarkDirty();
    }
}

// Changes on the host that the forecast packet carries but that do not roll anything.
[HarmonyPatch]
internal static class PrismaDisruptionChangePatch
{
    [HarmonyPatch(typeof(PrismaDisruptionModel), nameof(PrismaDisruptionModel.SetForecastUnlocked))]
    [HarmonyPostfix]
    private static void OnForecastUnlocked()
    {
        if (Main.Server.IsRunning)
            NetworkPrismaManager.MarkDirty();
    }

    [HarmonyPatch(typeof(PrismaDisruptionModel), nameof(PrismaDisruptionModel.Push))]
    [HarmonyPostfix]
    private static void OnLoaded()
    {
        if (Main.Server.IsRunning)
            NetworkPrismaManager.MarkDirty();
    }
}

// A client reaching a forecast unlock (its quest progress, from the shared events) asks the host
// to run it. The local unlock still runs; the host's forecast replaces the result shortly after.
[HarmonyPatch(typeof(PrismaDirector), nameof(PrismaDirector.OnDisruptionGroupUnlocked))]
internal static class OnPrismaDisruptionGroupUnlocked
{
    public static void Postfix([HarmonyArgument(0)] DisruptionAreaGroupConfig group)
    {
        try
        {
            if (HandlingPacket || Main.Server.IsRunning || !Main.Client.IsConnected)
                return;

            // Before the join snapshot this is the client's own save talking, not the shared world.
            if (!NetworkActorManager.CanSendActorChanges || !group)
                return;

            Main.Client.SendPacket(new PrismaGroupUnlockPacket { GroupName = group.name });
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not share a disruption forecast unlock: {ex.Message}");
        }
    }
}
