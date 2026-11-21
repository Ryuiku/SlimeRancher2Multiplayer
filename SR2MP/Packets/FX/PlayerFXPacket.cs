using SR2MP.Packets.Utils;

namespace SR2MP.Packets.FX;

internal sealed class PlayerFXPacket : IPacket
{
    internal enum PlayerFXType : byte
    {
        None,
        VacReject,
        VacHold,
        VacAccept,
        VacShoot,
        VacShootEmpty,
        WaterSplash,
        VacSlotChange,
        VacRunning,
        VacRunningStart,
        VacRunningEnd,
        VacShootSound,

        // Added at the end so the values above keep their numbers.
        VacPulse,
        VacPulseSound
    }

    public PlayerFXType FX;
    public Vector3 Position;
    // Only sent for VacPulse: the wave travels along the vac's aim, not along the world's axes.
    public Quaternion Rotation = Quaternion.identity;
    public string Player;

    public PacketType Type => PacketType.PlayerFX;
    public PacketReliability Reliability => PacketReliability.Unreliable;
    public NetworkChannel Channel => NetworkChannel.FX;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteEnum(FX);

        if (!IsPlayerSoundDictionary[FX])
        {
            writer.WriteVector3(Position);
            if (FX == PlayerFXType.VacPulse)
                writer.WriteQuaternion(Rotation);
        }
        else
            writer.WriteStringWithoutSize(Player);
    }

    public void Deserialise(PacketReader reader)
    {
        FX = reader.ReadEnum<PlayerFXType>();

        if (!IsPlayerSoundDictionary[FX])
        {
            Position = reader.ReadVector3();
            Rotation = FX == PlayerFXType.VacPulse ? reader.ReadQuaternion() : Quaternion.identity;
        }
        else
            Player = reader.ReadPooledStringOfSize(16)!;
    }
}