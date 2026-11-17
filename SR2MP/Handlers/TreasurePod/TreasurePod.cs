using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.TreasurePod;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.TreasurePod;

[PacketHandler((byte)PacketType.TreasurePod)]
internal sealed class TreasurePodHandler : BasePacketHandler<TreasurePodPacket>
{
    protected override bool Handle(TreasurePodPacket packet, IPEndPoint? _)
    {
        if (!string.IsNullOrEmpty(packet.ID))
            ApplyState(packet.ID, Il2Cpp.TreasurePod.State.OPEN, false);
        UnlockAppearance(packet);
        return true;
    }

    private static void UnlockAppearance(TreasurePodPacket packet)
    {
        if (packet.AppearanceSlime < 0)
            return;

        try
        {
            if (!ActorManager.ActorTypes.TryGetValue(packet.AppearanceSlime, out var type) || !type)
                return;

            var slime = type.TryCast<SlimeDefinition>();
            var director = SceneContext.Instance.SlimeAppearanceDirector;
            if (!slime || !director)
                return;

            var appearance = director.GetSpecificSlimeAppearance(type, packet.AppearanceSet);
            if (!appearance || director.IsAppearanceUnlocked(slime, appearance))
                return;

            HandlingPacket = true;
            director.UnlockAppearance(slime, appearance);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not unlock the appearance from pod {packet.ID}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }
    }

    /// <summary>
    /// Older builds sent pods by number and rebuilt the key as "pod" + number, which drops leading
    /// zeros ("pod0863429762" became "pod863429762"). Saves that joined with those builds hold a
    /// second record for such pods, and hosting one made every world send fail on the duplicate.
    /// This folds each extra record into the game's own one (opened wins) and removes it.
    /// </summary>
    internal static void RepairDuplicatePods()
    {
        try
        {
            var pods = GameState.pods;
            var byNumber = new Dictionary<long, List<string>>();

            foreach (var entry in pods)
            {
                var key = entry.key;
                if (key == null || !key.StartsWith("pod", StringComparison.Ordinal))
                    continue;

                if (!long.TryParse(key.Substring(3), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var number))
                    continue;

                if (!byNumber.TryGetValue(number, out var keys))
                    byNumber[number] = keys = new List<string>();
                keys.Add(key);
            }

            var removed = 0;
            foreach (var (number, keys) in byNumber)
            {
                if (keys.Count < 2)
                    continue;

                // The key the old builds made is exactly "pod" + number. Any other spelling of the
                // same number came from the game, so that one stays.
                var madeByOldBuild = "pod" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var keep = keys.FirstOrDefault(k => k != madeByOldBuild && pods[k]?.gameObj)
                           ?? keys.FirstOrDefault(k => k != madeByOldBuild)
                           ?? keys[0];

                var opened = keys.Any(k =>
                {
                    var model = pods[k];
                    return model?.state != null && model.state.Value == Il2Cpp.TreasurePod.State.OPEN;
                });

                if (opened)
                    ApplyState(keep, Il2Cpp.TreasurePod.State.OPEN, true);

                foreach (var key in keys)
                {
                    if (key == keep)
                        continue;

                    var model = pods[key];
                    if (model?.gameObj)
                        continue;

                    pods.Remove(key);
                    removed++;
                }
            }

            if (removed > 0)
                SrLogger.LogMessage($"Removed {removed} duplicate treasure pod record(s) left by an older build.");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not check treasure pods for duplicates: {ex.Message}");
        }
    }

    /// <summary>
    /// Records a pod's state and shows it. Does not run the pod's rewards: blueprints, upgrade
    /// components and spawned items are shared by their own packets (and a slime appearance comes
    /// with the pod packet), so running them here gave them out again on every machine. Works when the pod is not loaded here, and when this
    /// save has never seen the pod at all.
    /// </summary>
    internal static void ApplyState(string podKey, Il2Cpp.TreasurePod.State state, bool immediate)
    {
        try
        {
            if (!GameState.pods.TryGetValue(podKey, out var model) || model == null)
                model = GameState.InitializeTreasurePodModel(podKey);

            if (model == null)
                return;

            if (model.state == null)
                model.state = new ObservableValue<Il2Cpp.TreasurePod.State>(state);
            else if (model.state.Value != state)
                model.state.Set(state);

            if (!model.gameObj)
                return;

            var pod = model.gameObj.GetComponent<Il2Cpp.TreasurePod>();
            if (!pod)
                return;

            HandlingPacket = true;
            pod._forceUpdate = true;
            if (immediate)
                pod.UpdateImmediate(state);
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply treasure pod state for {podKey}: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }
    }
}