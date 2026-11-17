using Il2CppMonomiPark.SlimeRancher.Weather;
using SR2MP.Client.Managers;
using SR2MP.Packets.Utils;

namespace SR2MP.Packets.World;

/// <summary>
/// The host's whole weather forecast, every zone: upcoming entries as well as the running ones
/// (the map shows upcoming weather), whether the zone's forecast is unlocked, and the wind.
/// Clients never forecast or start weather themselves (the registry update is blocked there);
/// they take this as it is and only start or stop the states the host has running.
/// </summary>
internal sealed class WeatherPacket : IPacket
{
    public Dictionary<string, WeatherZoneData> Zones = new();

    public PacketType Type { get; private init; }
    public PacketReliability Reliability => PacketReliability.Reliable;
    public NetworkChannel Channel => NetworkChannel.Weather;

    public void Serialise(PacketWriter writer)
    {
        writer.WriteDictionary(Zones, PacketWriterDels.String, PacketWriterDels.NetObject<WeatherZoneData>.Writer);
    }

    public void Deserialise(PacketReader reader)
    {
        NetworkWeatherManager.CheckInitialized();
        Zones = reader.ReadDictionary(PacketReaderDels.String, PacketReaderDels.NetObject<WeatherZoneData>.Reader)!;
        Zones ??= new Dictionary<string, WeatherZoneData>();
    }

    public static WeatherPacket CreateFromRegistry(WeatherRegistry registry, PacketType type)
    {
        var packet = new WeatherPacket { Type = type };

        foreach (var pair in registry._zones)
        {
            var zoneKey = pair.Key;
            var zone = pair.Value;
            if (!zoneKey || string.IsNullOrEmpty(zoneKey.name) || zone == null)
                continue;

            var zoneData = new WeatherZoneData { ForecastUnlocked = zone.ForecastUnlocked };

            var parameters = zone.Parameters;
            if (parameters != null)
                zoneData.WindDirection = parameters.WindDirection;

            var forecast = zone.Forecast;
            if (forecast != null)
            {
                foreach (var entry in forecast)
                {
                    if (entry == null || entry.State == null)
                        continue;

                    var state = entry.State.TryCast<WeatherStateDefinition>();
                    if (!state)
                        continue;

                    var pattern = entry.Pattern;
                    zoneData.Forecasts.Add(new WeatherForecast
                    {
                        State = state!,
                        PatternName = pattern ? pattern.name : string.Empty,
                        Started = entry.Started,
                        StartTime = entry.StartTime,
                        EndTime = entry.EndTime,
                        IsEndTimeKnown = entry.IsEndTimeKnown
                    });
                }
            }

            packet.Zones[zoneKey.name] = zoneData;
        }

        return packet;
    }
}

internal sealed class WeatherZoneData : INetObject
{
    public bool ForecastUnlocked;
    public Vector3 WindDirection;
    public List<WeatherForecast> Forecasts = new();

    public void Serialise(PacketWriter writer)
    {
        writer.WriteBool(ForecastUnlocked);
        writer.WriteVector3(WindDirection);
        writer.WriteList(Forecasts, PacketWriterDels.NetObject<WeatherForecast>.Writer);
    }

    public void Deserialise(PacketReader reader)
    {
        ForecastUnlocked = reader.ReadBool();
        WindDirection = reader.ReadVector3();
        Forecasts = reader.ReadList(PacketReaderDels.NetObject<WeatherForecast>.Reader) ?? new List<WeatherForecast>();
    }
}

internal sealed class WeatherForecast : INetObject
{
    // Null on a client that does not know the state the host sent; such an entry is skipped.
    public WeatherStateDefinition? State;

    // The forecast entry's own pattern, by asset name, so the client does not have to guess it
    // from the state (several patterns share states, and zones have their own patterns).
    public string PatternName = string.Empty;

    public bool Started;
    public double StartTime;
    public double EndTime;
    public bool IsEndTimeKnown;

    public void Serialise(PacketWriter writer)
    {
        var id = State ? NetworkWeatherManager.GetPersistentID(State!) : 0;
        if (id == 0)
            SrLogger.LogDebug($"WeatherForecast: no persistence id for state {(State ? State!.name : "null")}");

        writer.WritePackedInt(id);
        writer.WriteString(PatternName);
        writer.WriteBool(Started);
        writer.WriteDouble(StartTime);
        writer.WriteDouble(EndTime);
        writer.WriteBool(IsEndTimeKnown);
    }

    public void Deserialise(PacketReader reader)
    {
        NetworkWeatherManager.CheckInitialized();
        var id = reader.ReadPackedInt();
        State = NetworkWeatherManager.WeatherStates.TryGetValue(id, out var state) ? state : null;
        PatternName = reader.ReadString() ?? string.Empty;
        Started = reader.ReadBool();
        StartTime = reader.ReadDouble();
        EndTime = reader.ReadDouble();
        IsEndTimeKnown = reader.ReadBool();
    }
}