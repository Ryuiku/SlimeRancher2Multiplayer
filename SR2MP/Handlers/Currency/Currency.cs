using System.Net;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Data;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Economy;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.Currency;

[PacketHandler((byte)PacketType.CurrencyAdjust)]
internal sealed class CurrencyHandler : BasePacketHandler<CurrencyPacket>
{
    protected override bool Handle(CurrencyPacket packet, IPEndPoint? _)
    {
        // By persistence id: the list position is not guaranteed to be id - 1.
        var currency = SR2MP.Shared.Utils.CurrencySnapshot.Find(packet.CurrencyType);
        if (currency == null)
        {
            SrLogger.LogWarning($"Currency {packet.CurrencyType} is unknown here; change skipped");
            return true;
        }

        var currencyDefinition = currency.Cast<ICurrency>();
        var difference = packet.NewAmount - SceneContext.Instance.PlayerState.GetCurrency(currencyDefinition);
        
        if (difference == 0)
            return true;

        IUIDisplayData? sourceOfChange = null;
        if (packet.SourceIdent != -1 && ActorManager.ActorTypes.TryGetValue(packet.SourceIdent, out var sourceType))
            sourceOfChange = sourceType.TryCast<IUIDisplayData>();

        HandlingPacket = true;
        try
        {
            if (difference < 0)
                SceneContext.Instance.PlayerState.SpendCurrency(currencyDefinition, -difference, sourceOfChange!);
            else
                SceneContext.Instance.PlayerState.AddCurrency(currencyDefinition, difference, packet.ShowUINotification, sourceOfChange!);
        }
        finally
        {
            HandlingPacket = false;
        }
        return true;
    }
}