using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController.Abilities;
using SR2MP.Packets.FX;

namespace SR2MP.Patches.FX;

// Shows a Pulse Wave to the other players: the shockwave at the vac and the sound at the avatar.
// Only a pulse that really went off is sent. A pulse restarts the ability's cooldown, and an
// attempt without energy does not, so a changed cooldown timer means it fired.
[HarmonyPatch(typeof(PulseWaveAbilityBehavior), nameof(PulseWaveAbilityBehavior.Start))]
internal static class OnPulseWave
{
    private static bool loggedMiss;

    public static void Prefix(PulseWaveAbilityBehavior __instance, out double __state)
    {
        __state = double.NaN;
        try
        {
            __state = __instance._cooldownTimer;
        }
        catch
        {
            // Read failed: the postfix sends nothing.
        }
    }

    public static void Postfix(PulseWaveAbilityBehavior __instance, double __state)
    {
        if (HandlingPacket || (!Main.Server.IsRunning && !Main.Client.IsConnected) || double.IsNaN(__state))
            return;

        try
        {
            // The timer is only set inside this call, so any real change is far above this.
            if (Math.Abs(__instance._cooldownTimer - __state) < 1e-9)
            {
                if (!loggedMiss)
                {
                    loggedMiss = true;
                    SrLogger.LogDebug("Pulse Wave started without changing its cooldown; not shown to the others");
                }
                return;
            }

            var origin = __instance.VacOrigin;
            if (origin)
            {
                Main.SendToAllOrServer(new PlayerFXPacket
                {
                    FX = PlayerFXType.VacPulse,
                    Position = origin.position,
                    Rotation = origin.rotation
                });
            }

            Main.SendToAllOrServer(new PlayerFXPacket
            {
                FX = PlayerFXType.VacPulseSound,
                Player = LocalID
            });
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not send a Pulse Wave: {ex.Message}");
        }
    }
}
