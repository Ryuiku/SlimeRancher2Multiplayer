using System.Collections.Concurrent;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using SR2MP.Packets.Utils;
using Starlight.Storage;

namespace SR2MP.Shared.Utils;

[InjectIntoIL]
internal sealed class MainThreadDispatcher : MonoBehaviour
{
    public static MainThreadDispatcher Instance { get; private set; }

    // ReSharper disable once InconsistentNaming
    private readonly ConcurrentQueue<Action> actionQueue = new();

    // ReSharper disable once InconsistentNaming
    private readonly ConcurrentQueue<ClientHandleCache> clientPacketQueue = new();

    // ReSharper disable once InconsistentNaming
    private readonly ConcurrentQueue<ServerHandleCache> serverPacketQueue = new();

    public static void Initialize()
    {
        if (Instance) return;

        var obj = new GameObject("SR2MP_MainThreadDispatcher");
        Instance = obj.AddComponent<MainThreadDispatcher>();
        DontDestroyOnLoad(obj);

        StartWatchdog();

        SrLogger.LogMessage("Main thread dispatcher initialized");
    }

    #region Watchdog

    // A host whose game stopped responding left no clue in its log. A background thread now notices
    // when the game has not finished a frame for a while and logs what the mod was doing at the
    // time (or that the game itself was busy), with how much network work was waiting.
    private static long lastFrameTicks = Environment.TickCount64;
    private static int packetsThisFrame;
    private static System.Threading.Thread? watchdog;

    // What the main thread is doing, kept cheap (no text per packet): a kind and the handler,
    // action or label it is on. Only turned into text when a stall is reported.
    private const int WorkGame = 0, WorkLabel = 1, WorkAction = 2, WorkHostPacket = 3, WorkPlayerPacket = 4;
    private static volatile int workKind;
    private static volatile object? workDetail = "starting";

    private static void SetWork(int kind, object? detail)
    {
        workDetail = detail;
        workKind = kind;
    }

    private static string DescribeWork()
    {
        var detail = workDetail;
        return workKind switch
        {
            WorkLabel => detail as string ?? "?",
            WorkAction => detail is Delegate d ? $"running a queued action ({d.Method.DeclaringType?.Name}.{d.Method.Name})" : "running a queued action",
            WorkHostPacket => $"handling a packet from the host ({detail?.GetType().Name})",
            WorkPlayerPacket => $"handling a packet from a player ({detail?.GetType().Name})",
            _ => "in the game's own code (not the mod's per-frame work)"
        };
    }

    /// <summary>
    /// Seconds since the game last finished a frame. Any thread.
    /// </summary>
    internal static double SecondsSinceLastFrame => (Environment.TickCount64 - Interlocked.Read(ref lastFrameTicks)) / 1000.0;

    private static void StartWatchdog()
    {
        if (watchdog != null)
            return;

        watchdog = new System.Threading.Thread(WatchdogLoop) { IsBackground = true, Name = "SR2MP main thread watchdog" };
        watchdog.Start();
    }

    private static void WatchdogLoop()
    {
        var reportedFor = 0L;

        while (true)
        {
            try
            {
                System.Threading.Thread.Sleep(1000);

                var stalled = Environment.TickCount64 - Interlocked.Read(ref lastFrameTicks);

                // Outside multiplayer the game may simply be paused in the background.
                var inSession = Main.Server.IsRunning || Main.Client.IsConnected;

                if (stalled < 8000 || !inSession)
                {
                    if (reportedFor > 0)
                    {
                        SrLogger.LogWarning($"Main thread is running again after about {reportedFor / 1000 + 1} s");
                        reportedFor = 0;
                    }

                    continue;
                }

                // At 8 s, 30 s, then every minute while it lasts.
                var due = reportedFor == 0 || (reportedFor < 30000 && stalled >= 30000) || stalled - reportedFor >= 60000;
                if (!due)
                    continue;

                reportedFor = stalled;

                var instance = Instance;
                var queued = instance != null
                    ? $"{instance.actionQueue.Count} actions, {instance.clientPacketQueue.Count} client and {instance.serverPacketQueue.Count} server packets waiting"
                    : "no dispatcher";

                var role = Main.Server.IsRunning ? "hosting" : Main.Client.IsConnected ? "connected to a host" : "not in multiplayer";

                SrLogger.LogWarning(
                    $"Main thread has not finished a frame for {stalled / 1000} s ({role}). " +
                    $"It was last: {DescribeWork()}. {packetsThisFrame} packets handled in that frame so far; {queued}.");
            }
            catch
            {
                // Never let the watchdog itself take anything down.
            }
        }
    }

    #endregion

    /// <summary>
    /// Keeps the game running while its window is not in front. A player who tabs out would
    /// otherwise stop simulating the slimes their game is running, for everyone. Any thread.
    /// </summary>
    internal static void KeepRunningInBackground()
    {
        if (!Instance)
            return;

        Instance.Enqueue(() =>
        {
            if (!Application.runInBackground)
            {
                Application.runInBackground = true;
                SrLogger.LogMessage("The game now keeps running while its window is in the background.");
            }
        });
    }

    public void Update()
    {
        Interlocked.Exchange(ref lastFrameTicks, Environment.TickCount64);
        packetsThisFrame = 0;

        // Process general actions
        while (actionQueue.TryDequeue(out var action))
        {
            try
            {
                SetWork(WorkAction, action);
                action.Invoke();
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"Error executing main thread action: {ex}");
            }
        }

        // Actor spawns and destroys that were held back while a zone was loading
        SetWork(WorkLabel, "applying actor changes held back during a zone load");
        SR2MP.Shared.Managers.NetworkActorManager.RunDeferredWorldChanges();
        SetWork(WorkLabel, "sending ranch, shop, feeder and market updates");

        // Joins and resyncs that waited for the host's zone load
        try
        {
            SR2MP.Shared.Managers.ReSyncManager.RunPendingSyncs();
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error sending a delayed world snapshot: {ex}");
        }

        // The host's Caretaker shop after a restock
        try
        {
            SR2MP.Shared.Managers.NetworkShopManager.Tick();
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error sending the shop: {ex}");
        }

        // How far the ranch areas awake here have been simulated, and who runs their catch-up
        try
        {
            SR2MP.Shared.Managers.NetworkRanchCellManager.Tick();
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error sending the ranch area times: {ex}");
        }

        // Auto-feeder schedules after this frame's feeding and catch-up
        try
        {
            SR2MP.Shared.Managers.NetworkFeederSync.Tick();
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error sending feeder schedules: {ex}");
        }

        // Market prices on the host after a sale
        try
        {
            SR2MP.Shared.Managers.MarketPriceSync.Tick();
        }
        catch (Exception ex)
        {
            SrLogger.LogError($"Error sending market prices: {ex}");
        }

        // Process client packets
        while (clientPacketQueue.TryDequeue(out var clientCache))
        {
            try
            {
                packetsThisFrame++;
                SetWork(WorkHostPacket, clientCache.Handler);
                clientCache.Handler.Handle(clientCache.Reader);
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"Error executing client packet handler: {ex}");
            }
            finally
            {
                HandlingPacket = false;
                PacketReader.Return(clientCache.Reader);
            }
        }

        // Process server packets
        while (serverPacketQueue.TryDequeue(out var serverCache))
        {
            try
            {
                // A packet that was still on its way in when the server closed is thrown away:
                // handled now, an ownership claim would freeze an actor nobody moves anymore.
                packetsThisFrame++;
                SetWork(WorkPlayerPacket, serverCache.Handler);

                if (Main.Server.IsRunning)
                    serverCache.Handler.Handle(serverCache.Reader, serverCache.ClientEp);
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"Error executing server packet handler: {ex}");
            }
            finally
            {
                HandlingPacket = false;
                PacketReader.Return(serverCache.Reader);
            }
        }

        SetWork(WorkGame, null);
    }

    [HideFromIl2Cpp]
    public void Enqueue(Action action) => actionQueue.Enqueue(action);

    [HideFromIl2Cpp]
    public void Enqueue(in ClientHandleCache cache) => clientPacketQueue.Enqueue(cache);

    [HideFromIl2Cpp]
    public void Enqueue(in ServerHandleCache cache) => serverPacketQueue.Enqueue(cache);

    /// <summary>
    /// Throws away client packets that were received but not handled yet. Main thread only.
    /// </summary>
    [HideFromIl2Cpp]
    public void DropClientPackets()
    {
        while (clientPacketQueue.TryDequeue(out var cache))
            PacketReader.Return(cache.Reader);
    }

    /// <summary>
    /// Throws away server packets that were received but not handled yet. Main thread only.
    /// </summary>
    [HideFromIl2Cpp]
    public void DropServerPackets()
    {
        while (serverPacketQueue.TryDequeue(out var cache))
            PacketReader.Return(cache.Reader);
    }

    public void OnDestroy()
    {
        Instance = null!;

        while (clientPacketQueue.TryDequeue(out var c))
            PacketReader.Return(c.Reader);

        while (serverPacketQueue.TryDequeue(out var s))
            PacketReader.Return(s.Reader);
    }
}