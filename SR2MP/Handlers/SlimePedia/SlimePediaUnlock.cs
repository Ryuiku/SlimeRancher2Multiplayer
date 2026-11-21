using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Pedia;
using SR2MP.Packets.Utils;

namespace SR2MP.Handlers.SlimePedia;

[PacketHandler((byte)PacketType.PediaUnlock)]
internal sealed class SlimePediaUnlockHandler : BasePacketHandler<PediaUnlockPacket>
{
    protected override bool Handle(PediaUnlockPacket packet, IPEndPoint? _)
    {
        // An entry this game doesn't know (another game version) is skipped but still passed on;
        // the indexer used to throw, so the host never relayed the unlock to anyone else.
        var lookup = GameContext.Instance.AutoSaveDirector._saveReferenceTranslation._pediaEntryLookup;
        if (string.IsNullOrEmpty(packet.ID) || !lookup.TryGetValue(packet.ID, out var entry) || !entry)
        {
            SrLogger.LogDebug($"Pedia entry {packet.ID} is unknown here; skipped");
            return true;
        }

        HandlingPacket = true;
        try
        {
            SceneContext.Instance.PediaDirector.Unlock(entry, packet.Popup);
        }
        finally
        {
            HandlingPacket = false;
        }

        return true;
    }
}