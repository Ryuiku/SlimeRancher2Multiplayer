using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Labyrinth;

[PacketHandler((byte)PacketType.InitialPlortDepositors, HandlerType.Client)]
internal sealed class InitialPlortDepositorsHandler : BasePacketHandler<InitialPlortDepositorsPacket>
{
    protected override bool Handle(InitialPlortDepositorsPacket packet, IPEndPoint? _)
    {
        var hostIds = new HashSet<string>();
        foreach (var depositor in packet.Depositors)
            hostIds.Add(depositor.ID);

        SR2MP.Shared.Utils.HostWorldState.DropEntriesHostLacks(
            GameState.depositors, hostIds, m => m._gameObject, "plort depositors");

        HandlingPacket = true;

        try
        {
            foreach (var depositor in packet.Depositors)
            {
                // One depositor failing must not stop every depositor after it from loading.
                try
                {
                    NetworkPlortDepositorManager.ApplyState(depositor.ID, depositor.AmountDeposited, true);
                }
                catch (Exception ex)
                {
                    SrLogger.LogWarning($"Could not load plort depositor {depositor.ID}: {ex.Message}");
                }
            }
        }
        finally
        {
            HandlingPacket = false;
        }

        return false;
    }
}
