using System.Net;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SR2MP.Api;
using SR2MP.Packets.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Internal;

[PacketHandler((byte)PacketType.ModSyncAck, HandlerType.Server)]
internal sealed class ModSyncAckHandler : BasePacketHandler<ModSyncPacket>
{
    protected override bool Handle(ModSyncPacket packet, IPEndPoint? clientEp)
    {
        // Everyone in a session needs the same Ranching Together build: the network messages
        // differ between versions, so a mismatched player would desync instead of failing cleanly.
        var clientVersion = packet.Mods.Remove(ModSyncPacket.SelfVersionKey, out var self)
            ? self.Version
            : "0.3.9 or older (the original mod)";

        if (clientVersion != BuildInfo.Version)
        {
            SrLogger.LogMessage($"Turned away a player on Ranching Together {clientVersion} (host runs {BuildInfo.Version})");

            var versionDeny = new ConnectionDenyPacket
            {
                Reason = $"The host runs Ranching Together {BuildInfo.Version} and you run {clientVersion}. " +
                         $"Everyone needs the same version: {BuildInfo.Releases}"
            };
            Main.Server.SendToClient(versionDeny, clientEp!);
            Main.Server.ClientManager.RemoveClient(clientEp!);

            return false;
        }

        var clientNeeds = new List<string>();
        var hostNeeds = new List<string>();

        foreach (var (id, data) in packet.Mods)
        {
            if (!ApiHandlers.Holders.ContainsKey(id))
                hostNeeds.Add($"{data.Name} ({data.Version})");
        }

        foreach (var (id, holder) in ApiHandlers.Holders)
        {
            if (!packet.Mods.ContainsKey(id))
                clientNeeds.Add($"{holder.Mod.Info.Name} ({holder.Mod.Info.Version})");
        }

        if (clientNeeds.Count > 0 || hostNeeds.Count > 0)
        {
            SrLogger.LogMessage($"Mods desynchronized!\n\tClient Needs: {string.Join(", ", clientNeeds)}\n\tHost Needs: {string.Join(", ", hostNeeds)}");

            var reason = $"You have incompatible mods!\nMods that are missing or on the wrong version:\n {string.Join("\n", clientNeeds)}";
            var denyPacket = new ConnectionDenyPacket { Reason = reason };
            Main.Server.SendToClient(denyPacket, clientEp!);

            Main.Server.ClientManager.RemoveClient(clientEp!);

            return false;
        }

        var netData = new List<ModNetData>();

        foreach (var id in ApiHandlers.Holders.Keys)
        {
            netData.Add(new ModNetData
            {
                ModId = id,
                NetId = ApiHandlers.GetOrIncrementNetId(id)
            });
        }

        ApiHandlers.RefreshPacketMapping();

        var money = SceneContext.Instance.PlayerState.GetCurrency(
            GameContext.Instance.LookupDirector._currencyList[0].Cast<ICurrency>());
        var rainbowMoney = SceneContext.Instance.PlayerState.GetCurrency(
            GameContext.Instance.LookupDirector._currencyList[1].Cast<ICurrency>());

        var ackPacket = new ConnectionApprovePacket
        {
            InitialJoin = true,
            PlayerId = packet.PlayerId,
            OtherPlayers = Array.ConvertAll(PlayerManager.GetAllPlayers().ToArray(),
                p => (p.PlayerId, p.Username)),
            Money = money,
            RainbowMoney = rainbowMoney,
            AllowCheats = Main.AllowCheats,
            NetData = netData,
            Currencies = SR2MP.Shared.Utils.CurrencySnapshot.Collect()
        };

        // The connectAck is different because of initialJoin, otherwise another PlayerJoin request will be sent

        Main.Server.SendToClient(ackPacket, clientEp!);
        
        ActorManager.SendActorTypeRegistry(clientEp!);

        ReSyncManager.SynchronizeClient(packet.PlayerId, clientEp!);

        SrLogger.LogMessage(
            $"Player {packet.PlayerId} successfully connected",
            $"Player {packet.PlayerId} successfully connected from {clientEp}");

        return false;
    }
}