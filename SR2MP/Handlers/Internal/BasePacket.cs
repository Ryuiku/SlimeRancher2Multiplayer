using System.Net;
using System.Runtime.CompilerServices;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Internal;

internal abstract class BasePacketHandler<T> : IClientPacketHandler, IServerPacketHandler where T : IPacket, new()
{
    public bool IsServerSide { protected get; set; }

    // Do NOT override these! Only the ApiHandler class should be dealing with this!
    public virtual void Handle(PacketReader reader)
    {
        if (!IsServerSide)
            ProcessPacket(reader, null);
    }

    public virtual void Handle(PacketReader reader, IPEndPoint? clientEp)
    {
        if (IsServerSide)
            ProcessPacket(reader, clientEp);
    }

    private void ProcessPacket(PacketReader reader, IPEndPoint? clientEp)
    {
        var packet = reader.ReadPacket<T>();

        // Counted for the zone load trace: which packets were applied while a zone was loading.
        var duringLoad = SR2MP.Shared.Managers.NetworkActorManager.IsSceneLoadingNow;
        if (duringLoad)
            SR2MP.Shared.Utils.SceneLoadTrace.CountHandledDuringLoad(typeof(T).Name);

        bool shouldSend;

        try
        {
            shouldSend = Handle(packet, clientEp);
        }
        catch (Exception ex)
        {
            HandlingPacket = false;
            if (duringLoad)
                SR2MP.Shared.Utils.SceneLoadTrace.Record($"{typeof(T).Name} handler threw during a load: {ex.Message}");
            SrLogger.LogError($"Packet handler for {typeof(T).Name} threw: {ex}");
            return;
        }

        if (IsServerSide && shouldSend)
            PacketSender.SendToAllExcept(packet, clientEp);
    }

    protected abstract bool Handle(T packet, IPEndPoint? clientEp);
}

internal static class PacketSender
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SendPacket<T>(T packet) where T : IPacket
        => Main.Client.SendPacket(packet);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SendToAllExcept<T>(T packet, IPEndPoint? excludedEp) where T : IPacket
        => Main.Server.SendToAllExcept(packet, excludedEp);
}