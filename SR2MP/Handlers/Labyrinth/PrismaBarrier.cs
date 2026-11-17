using System.Net;
using Il2CppMonomiPark.SlimeRancher.Labyrinth;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.World;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Labyrinth;

[PacketHandler((byte)PacketType.PrismaBarrier)]
internal sealed class PrismaBarrierHandler : BasePacketHandler<PrismaBarrierPacket>
{
    protected override bool Handle(PrismaBarrierPacket packet, IPEndPoint? _)
    {
        // Barrier times come from the host only. A client on an older build may still send its
        // own; it is neither applied nor passed on.
        if (IsServerSide)
        {
            SrLogger.LogDebug($"Ignored a prisma barrier time from a client ({packet.ID})");
            return false;
        }

        HandlingPacket = true;
        try
        {
            var barriers = GameState.AllPrismaBarriers();
            if (barriers.TryGetValue(packet.ID, out var model) && model != null)
            {
                model.ActivationTime = packet.ActivationTime;

                if (model._gameObj)
                {
                    model.NotifyParticipants();

                    var barrier = model._gameObj.GetComponent<PrismaBarrier>();
                    if (barrier)
                        barrier.SetActivationTime(packet.ActivationTime);
                }
            }
            else
            {
                // Not known here yet (area never loaded): record it, as the join packet does.
                barriers[packet.ID] = new Il2CppMonomiPark.SlimeRancher.DataModel.PrismaBarrierModel(packet.ID)
                {
                    _gameObj = null,
                    ActivationTime = packet.ActivationTime
                };
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply prisma barrier {packet.ID}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}
