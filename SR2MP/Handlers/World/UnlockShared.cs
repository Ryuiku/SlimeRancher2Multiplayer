using System.Net;
using SR2MP.Handlers.Internal;
using SR2MP.Packets.Utils;
using SR2MP.Packets.World;
using SR2MP.Shared.Utils;

namespace SR2MP.Handlers.World;

[PacketHandler((byte)PacketType.UnlockShared)]
internal sealed class UnlockSharedHandler : BasePacketHandler<UnlockSharedPacket>
{
    protected override bool Handle(UnlockSharedPacket packet, IPEndPoint? _)
    {
        // Always passed on, even when it is not new here or unknown: it can still be new for the others.
        try
        {
            switch (packet.Kind)
            {
                case UnlockSharedPacket.UnlockKind.Palette:
                    UnlockLookup.ApplyPalette(packet.RefId);
                    break;

                case UnlockSharedPacket.UnlockKind.Theme:
                    UnlockLookup.ApplyTheme(packet.RefId, packet.Owned);
                    break;

                case UnlockSharedPacket.UnlockKind.Appearance:
                    UnlockLookup.UnlockAppearance(packet.IdentId, (SlimeAppearance.AppearanceSaveSet)packet.SaveSet);
                    break;

                case UnlockSharedPacket.UnlockKind.SecretStyle:
                    UnlockLookup.DiscoverSecretStyle(packet.IdentId);
                    break;
            }
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not apply unlock {packet.Kind} {packet.RefId}{packet.IdentId}: {ex.Message}");
        }

        return true;
    }
}
