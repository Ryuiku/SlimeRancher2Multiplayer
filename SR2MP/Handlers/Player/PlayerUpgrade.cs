using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Upgrade;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Player;

[PacketHandler((byte)PacketType.PlayerUpgrade)]
internal sealed class PlayerUpgradeHandler : BasePacketHandler<PlayerUpgradePacket>
{
    protected override bool Handle(PlayerUpgradePacket packet, IPEndPoint? _)
    {
        var model = SceneContext.Instance.PlayerState._model.upgradeModel;

        var upgrade = model.upgradeDefinitions.items._items.FirstOrDefault(
            x => x != null && x._uniqueId == packet.UpgradeID);

        // Unknown here: still passed on to the other players.
        if (upgrade == null)
            return true;

        HandlingPacket = true;
        try
        {
            // Up to the sender's level, not "one more": a player who already has it stays where they are.
            for (var i = 0; i < 16; i++)
            {
                var before = model.GetUpgradeLevel(upgrade);
                if (before >= packet.Level)
                    break;

                model.IncrementUpgradeLevel(upgrade);

                if (model.GetUpgradeLevel(upgrade) <= before)
                    break;
            }
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}