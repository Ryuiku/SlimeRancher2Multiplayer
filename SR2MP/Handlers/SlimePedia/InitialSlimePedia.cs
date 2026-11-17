using System.Net;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Loading;
using SR2MP.Packets.Utils;
using Enumerable = Il2CppSystem.Linq.Enumerable;

namespace SR2MP.Handlers.SlimePedia;

[PacketHandler((byte)PacketType.InitialPediaEntries, HandlerType.Client)]
internal sealed class InitialSlimePediaLoadHandler : BasePacketHandler<InitialPediaPacket>
{
    protected override bool Handle(InitialPediaPacket packet, IPEndPoint? _)
    {
        // One id this game doesn't know used to throw and lose the whole snapshot; those are
        // skipped and counted instead.
        var lookup = GameContext.Instance.AutoSaveDirector._saveReferenceTranslation._pediaEntryLookup;
        var unlocked = new List<PediaEntry>(packet.Entries.Count);
        var skipped = 0;
        foreach (var id in packet.Entries)
        {
            if (!string.IsNullOrEmpty(id) && lookup.TryGetValue(id, out var entry) && entry)
                unlocked.Add(entry);
            else
                skipped++;
        }

        if (skipped > 0)
            SrLogger.LogMessage($"Pedia: {skipped} of the host's entries are unknown here and were skipped.");

        // The host's pedia replaces this one: the host's save is the source of truth.
        var unlockedCpp = new Il2CppReferenceArray<PediaEntry>(unlocked.ToArray());
        var model = SceneContext.Instance.PediaDirector._pediaModel;
        model.unlocked = Enumerable.ToHashSet(
            unlockedCpp.Cast<CppCollections.IEnumerable<PediaEntry>>());

        // Tell the director, so the pedia screen and what reacts to unlocks are current without
        // a reload.
        HandlingPacket = true;
        try
        {
            model.NotifyParticipants();
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Pedia refresh after join failed: {ex.Message}");
        }
        finally
        {
            HandlingPacket = false;
        }

        return false;
    }
}