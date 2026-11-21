using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Gadget;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Gadget;

[PacketHandler((byte)PacketType.Blueprints)]
internal sealed class BlueprintsHandler : BasePacketHandler<BlueprintsPacket>
{
    protected override bool Handle(BlueprintsPacket packet, IPEndPoint? _)
    {
        var director = SceneContext.Instance ? SceneContext.Instance.GadgetDirector : null;
        if (!director)
            return true;

        foreach (var id in packet.Blueprints)
        {
            try
            {
                if (!ActorManager.ActorTypes.TryGetValue(id, out var type) || type == null)
                    continue;

                var definition = type.TryCast<GadgetDefinition>();
                if (definition == null || director!.HasBlueprint(definition))
                    continue;

                HandlingPacket = true;
                director.AddBlueprint(definition, false);
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not add blueprint {id}: {ex.Message}");
            }
            finally
            {
                HandlingPacket = false;
            }
        }

        return true;
    }
}
