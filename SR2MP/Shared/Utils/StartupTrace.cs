using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace SR2MP.Shared.Utils;

/// <summary>
/// Startup diagnostics: logs each type this mod injects into the game and each game method it
/// patches, as it happens. A crash while the game starts then shows in the MelonLoader log right
/// after the step that caused it. Runs when the mod's assembly is first used, before Starlight
/// injects and patches it.
/// </summary>
internal static class StartupTrace
{
    private static bool installed;
    private static Assembly? ownAssembly;

    [ModuleInitializer]
    internal static void Install()
    {
        if (installed)
            return;
        installed = true;
        ownAssembly = typeof(StartupTrace).Assembly;

        try
        {
            var harmony = new HarmonyLib.Harmony("SR2MP.StartupTrace");

            var processorPatch = AccessTools.Method(typeof(PatchClassProcessor), nameof(PatchClassProcessor.Patch));
            if (processorPatch != null)
                harmony.Patch(processorPatch,
                    prefix: new HarmonyMethod(typeof(StartupTrace), nameof(BeforePatchClass)),
                    postfix: new HarmonyMethod(typeof(StartupTrace), nameof(AfterPatchClass)));

            var updateWrapper = AccessTools.Method("HarmonyLib.PatchFunctions:UpdateWrapper");
            if (updateWrapper != null)
                harmony.Patch(updateWrapper, prefix: new HarmonyMethod(typeof(StartupTrace), nameof(BeforeUpdateWrapper)));

            var register = AccessTools.Method(typeof(ClassInjector), nameof(ClassInjector.RegisterTypeInIl2Cpp),
                new[] { typeof(Type), typeof(RegisterTypeOptions) });
            if (register != null)
                harmony.Patch(register, prefix: new HarmonyMethod(typeof(StartupTrace), nameof(BeforeRegister)));

            MelonLoader.MelonLogger.Msg("[SR2MP startup] tracing injection and patching");
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"[SR2MP startup] trace not installed: {ex.Message}");
        }
    }

    private static bool Ours(Type? type) => type != null && type.Assembly == ownAssembly;

    private static bool patchingOurs;

    private static void BeforePatchClass(Type ___containerType)
    {
        patchingOurs = Ours(___containerType);
        if (patchingOurs)
            MelonLoader.MelonLogger.Msg($"[SR2MP startup] patching {___containerType.FullName}");
    }

    private static void AfterPatchClass(Type ___containerType)
    {
        patchingOurs = false;
        if (Ours(___containerType))
            MelonLoader.MelonLogger.Msg($"[SR2MP startup] patched {___containerType.FullName}");
    }

    private static void BeforeUpdateWrapper(MethodBase original)
    {
        // Only while one of this mod's patch classes is being applied.
        if (!patchingOurs)
            return;

        try
        {
            MelonLoader.MelonLogger.Msg($"[SR2MP startup]   -> {original?.DeclaringType?.FullName}.{original?.Name}");
        }
        catch
        {
            // diagnostics only
        }
    }

    private static void BeforeRegister(Type type)
    {
        if (Ours(type))
            MelonLoader.MelonLogger.Msg($"[SR2MP startup] injecting {type.FullName}");
    }
}
