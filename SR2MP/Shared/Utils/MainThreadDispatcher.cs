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

        SrLogger.LogMessage("Main thread dispatcher initialized");
    }

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
        // Process general actions
        while (actionQueue.TryDequeue(out var action))
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                SrLogger.LogError($"Error executing main thread action: {ex}");
            }
        }

        // Actor spawns and destroys that were held back while a zone was loading
        SR2MP.Shared.Managers.NetworkActorManager.RunDeferredWorldChanges();

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

        // Process client packets
        while (clientPacketQueue.TryDequeue(out var clientCache))
        {
            try
            {
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