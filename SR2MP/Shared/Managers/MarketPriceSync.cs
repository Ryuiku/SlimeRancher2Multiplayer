using SR2MP.Packets.Economy;

namespace SR2MP.Shared.Managers;

/// <summary>
/// Host only. After a sale (its own or a client's), checks every couple of seconds whether any
/// market price changed and, if so, sends all prices. Prices that only move at the daily reset
/// are already sent by the reset; this covers any change a sale makes right away.
/// </summary>
internal static class MarketPriceSync
{
    private const float CheckInterval = 2f;

    private static bool dirty;
    private static float nextCheck;
    private static (float Current, float Previous)[]? lastSent;

    static MarketPriceSync()
    {
        Main.Server.OnServerStarted +=      ClearSession;
        Main.Client.OnConnected     += _ => ClearSession();
        Main.Client.OnDisconnected  +=      ClearSession;
    }

    private static void ClearSession()
    {
        dirty = false;
        lastSent = null;
    }

    internal static void MarkDirty() => dirty = true;

    /// <summary>The prices just sent another way (the daily reset, a join).</summary>
    internal static void Remember((float Current, float Previous)[]? prices) => lastSent = prices;

    /// <summary>Called every frame on the main thread.</summary>
    internal static void Tick()
    {
        if (!dirty || !Main.Server.IsRunning)
            return;

        var now = Time.realtimeSinceStartup;
        if (now < nextCheck)
            return;
        nextCheck = now + CheckInterval;
        dirty = false;

        var prices = MarketPricesArray;
        if (prices == null)
            return;

        if (lastSent != null && lastSent.AsSpan().SequenceEqual(prices))
            return;

        lastSent = prices;
        Main.SendToAllOrServer(new MarketPricePacket { Prices = prices });
    }
}
