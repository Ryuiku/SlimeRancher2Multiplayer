using System.Net;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.World;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Access;

[PacketHandler((byte)PacketType.InitialSwitches, HandlerType.Client)]
internal sealed class InitialSwitchesHandler : BasePacketHandler<InitialSwitchesPacket>
{
    protected override bool Handle(InitialSwitchesPacket packet, IPEndPoint? _)
    {
        var gameModel = GameState;

        var hostIds = new HashSet<string>();
        foreach (var worldSwitch in packet.Switches)
            hostIds.Add(worldSwitch.ID);

        SR2MP.Shared.Utils.HostWorldState.DropEntriesHostLacks(
            gameModel.switches, hostIds, m => m.gameObj, "switches");

        foreach (var worldSwitch in packet.Switches)
        {
            // One switch failing must not stop every switch after it from loading.
            try
            {
            if (gameModel.switches.TryGetValue(worldSwitch.ID, out var switchModel))
            {
                switchModel.state = worldSwitch.State;

                if (!switchModel.gameObj)
                    continue;

                var switchComponentBase = switchModel.gameObj.GetComponent<WorldSwitchModel.Participant>();

                HandlingPacket = true;
                switchComponentBase.SetModel(switchModel);

                var primary = switchComponentBase.TryCast<WorldStatePrimarySwitch>();
                var secondary = switchComponentBase.TryCast<WorldStateSecondarySwitch>();
                var invisible = switchComponentBase.TryCast<WorldStateInvisibleSwitch>();

                HandlingPacket = true;
                primary?.SetStateForAll(worldSwitch.State, true);
                secondary?.SetState(worldSwitch.State, true);
                invisible?.SetStateForAll(worldSwitch.State, true);
                HandlingPacket = false;
            }
            else
            {
                switchModel = new WorldSwitchModel
                {
                    gameObj = null,
                    state = worldSwitch.State
                };

                gameModel.switches.Add(worldSwitch.ID, switchModel);
            }
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not load switch {worldSwitch.ID}: {ex.Message}");
            }
            finally
            {
                HandlingPacket = false;
            }
        }

        return false;
    }
}