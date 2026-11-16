using System.Collections;
using System.Net;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SR2MP.Api;
using SR2MP.Components.Player;
using SR2MP.Components.UI;
using SR2MP.Packets.Internal;
using SR2MP.Packets.Player;
using SR2MP.Packets.Utils;
using SR2MP.Shared.Managers;

namespace SR2MP.Handlers.Internal;

[PacketHandler((byte)PacketType.ConnectionApprove, HandlerType.Client)]
internal sealed class ConnectionApproveHandler : BasePacketHandler<ConnectionApprovePacket>
{
    protected override bool Handle(ConnectionApprovePacket packet, IPEndPoint? _)
    {
        if (packet.InitialJoin)
        {
            var joinPacket = new PlayerJoinPacket
            {
                Type = PacketType.PlayerJoin,
                PlayerId = packet.PlayerId,
                PlayerName = Main.Username
            };

            PacketSender.SendPacket(joinPacket);

            // SR2MPClient.StartHeartbeat();
            Main.Client.NotifyConnected();

            SrLogger.LogMessage($"Connection acknowledged by server! (PlayerId: {packet.PlayerId})");

            CheatsEnabled = packet.AllowCheats;

            // todo: resync players too
            foreach (var (id, username) in packet.OtherPlayers)
                SpawnPlayer(id, username);

            StartCoroutine(WarnIfWorldNotLoaded());
        }

        HandlingPacket = true;
        SceneContext.Instance.PlayerState._model.SetCurrency(GameContext.Instance.LookupDirector._currencyList[0].Cast<ICurrency>(), packet.Money);
        SceneContext.Instance.PlayerState._model.SetCurrency(GameContext.Instance.LookupDirector._currencyList[1].Cast<ICurrency>(), packet.RainbowMoney);
        HandlingPacket = false;

        foreach (var data in packet.NetData)
        {
            ApiHandlers.SetNetId(data.ModId, data.NetId);
        }

        ApiHandlers.RefreshPacketMapping();

        return false;
    }

    // If the host's actors never arrive, this client can't sync any slimes or items (on purpose, see
    // NetworkActorManager.CanSendActorChanges), so tell the player instead of failing silently.
    private static IEnumerator WarnIfWorldNotLoaded()
    {
        var waited = 0f;

        while (waited < 90f)
        {
            if (!Main.Client.IsConnected || NetworkActorManager.InitialActorsLoaded)
                yield break;

            waited += UnityEngine.Time.unscaledDeltaTime;
            yield return null;
        }

        if (!Main.Client.IsConnected || NetworkActorManager.InitialActorsLoaded)
            yield break;

        SrLogger.LogWarning("The host's world data has not arrived after 90 seconds, slimes and items are not syncing.");
        MultiplayerUI.Instance.RegisterSystemMessage(
            "The world didn't finish loading, slimes and items won't sync. Try Resync, or rejoin.",
            $"SYSTEM_WORLD_NOT_LOADED_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            MultiplayerUI.SystemMessageDisconnect);
    }

    private static void SpawnPlayer(string id, string name)
    {
        var playerObject = Object.Instantiate(PlayerPrefab).GetComponent<NetworkPlayer>();
        playerObject.gameObject.SetActive(true);
        playerObject.ID = id;
        playerObject.gameObject.name = id;
        PlayerObjects.Add(id, playerObject.gameObject);
        PlayerManager.AddPlayer(id).Username = name;
        Object.DontDestroyOnLoad(playerObject);
    }
}