using Il2CppMonomiPark.SlimeRancher.Economy;

namespace SR2MP.Shared.Utils;

/// <summary>
/// All currencies of this save, for the join snapshot, and the lookup of a currency by its
/// persistence id. Only newbucks and rainbow coins used to be sent, so any other currency
/// (sprinkles) kept the joiner's own amount until the first change overwrote everyone's.
/// </summary>
internal static class CurrencySnapshot
{
    internal static List<(int Id, int Amount, int EverCollected)> Collect()
    {
        var list = new List<(int Id, int Amount, int EverCollected)>();
        try
        {
            var currencies = GameContext.Instance.LookupDirector._currencyList;
            var model = SceneContext.Instance.PlayerState._model;
            for (var i = 0; i < currencies.Count; i++)
            {
                var definition = currencies[i];
                if (!definition)
                    continue;

                var currency = definition.Cast<ICurrency>();
                list.Add((definition.PersistenceId,
                    model.GetCurrencyAmount(currency),
                    model.GetCurrencyAmountEverCollected(currency)));
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not read all currencies: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// The currency with this persistence id. Falls back to the list position the live packet
    /// used to assume (id - 1), in case a currency reports no id.
    /// </summary>
    internal static CurrencyDefinition? Find(int persistenceId)
    {
        var currencies = GameContext.Instance.LookupDirector._currencyList;
        for (var i = 0; i < currencies.Count; i++)
        {
            var definition = currencies[i];
            if (definition && definition.PersistenceId == persistenceId)
                return definition;
        }

        var index = persistenceId - 1;
        if (index >= 0 && index < currencies.Count && currencies[index])
            return currencies[index];

        return null;
    }

    /// <summary>Writes the host's amounts here. Returns how many were applied.</summary>
    internal static int Apply(List<(int Id, int Amount, int EverCollected)> entries)
    {
        var applied = 0;
        var model = SceneContext.Instance.PlayerState._model;
        foreach (var (id, amount, everCollected) in entries)
        {
            try
            {
                var definition = Find(id);
                if (definition == null)
                {
                    SrLogger.LogWarning($"The host has currency {id}, which this game doesn't know; skipped");
                    continue;
                }

                model.SetCurrencyAndAmountEverCollected(definition.Cast<ICurrency>(), amount, everCollected);
                applied++;
            }
            catch (Exception ex)
            {
                SrLogger.LogWarning($"Could not set currency {id}: {ex.Message}");
            }
        }

        return applied;
    }
}
