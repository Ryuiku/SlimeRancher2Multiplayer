// Copied from https://github.com/saltacc/'s PR to https://github.com/pyeight/SlimeRancher2Multiplayer/
// https://github.com/pyeight/SlimeRancher2Multiplayer/pull/32/

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SR2MP.Shared.Utils;

public static class JoinCode
{
    private const int Ipv4Bytes = 6;
    private const int Ipv6Bytes = 18;
    private const int Ipv4CodeLength = 9;
    private const int Ipv6CodeLength = 25;

    // Public IPv4 + port, then the host's LAN IPv4 + local port. Lets players on the host's home
    // network connect directly, since most routers can't connect to their own public IP (no NAT hairpinning).
    private const int Ipv4LanBytes = 12;
    private const int Ipv4LanCodeLength = 17;

    /// <summary>
    /// Encodes the public address, plus this machine's LAN address when it has one.
    /// </summary>
    public static string EncodeWithLan(IPAddress address, ushort port, ushort localPort)
    {
        var lan = GetLocalLanAddress();
        var bytes = address.GetAddressBytes();

        if (lan == null || bytes.Length != 4 || lan.Equals(address))
            return Encode(address, port);

        var data = new byte[Ipv4LanBytes];
        Buffer.BlockCopy(bytes, 0, data, 0, 4);
        data[4] = (byte)(port >> 8);
        data[5] = (byte)port;
        Buffer.BlockCopy(lan.GetAddressBytes(), 0, data, 6, 4);
        data[10] = (byte)(localPort >> 8);
        data[11] = (byte)localPort;

        return Base58.Encode(data).PadLeft(Ipv4LanCodeLength, '1');
    }

    public static bool TryDecode(string code, out IPAddress address, out ushort port, out IPEndPoint? lanEndPoint, out string error)
    {
        lanEndPoint = null;

        if (code != null && code.Trim().Length == Ipv4LanCodeLength)
        {
            address = null!;
            port = 0;
            error = string.Empty;

            if (!TryDecodeBytes(code.Trim(), Ipv4LanBytes, out var decoded, out error))
                return false;

            port = (ushort)((decoded[4] << 8) | decoded[5]);
            var lanPort = (ushort)((decoded[10] << 8) | decoded[11]);

            if (port == 0 || lanPort == 0)
            {
                error = "Join code contains an invalid port.";
                return false;
            }

            address = new IPAddress(decoded.AsSpan(0, 4));
            lanEndPoint = new IPEndPoint(new IPAddress(decoded.AsSpan(6, 4)), lanPort);
            return true;
        }

        return TryDecode(code!, out address, out port, out error);
    }

    /// <summary>
    /// True if the given LAN address looks like it is on one of this machine's networks, so it is
    /// worth trying before the public address. A wrong "yes" (a VPN or virtual machine adapter that
    /// happens to use the same range) only costs a few seconds before the public address is tried;
    /// a wrong "no" makes players on the host's own network fail on routers without NAT hairpinning.
    /// </summary>
    public static bool IsOnSameLan(IPAddress lanAddress)
    {
        try
        {
            var target = lanAddress.GetAddressBytes();
            if (target.Length != 4)
                return false;

            var homeMask = new byte[] { 255, 255, 255, 0 };

            foreach (var unicast in GetUsableIpv4Addresses())
            {
                var mask = unicast.IPv4Mask?.GetAddressBytes();
                if (mask == null || mask.Length != 4 || mask.All(b => b == 0))
                    mask = homeMask;

                if (SameSubnet(unicast.Address.GetAddressBytes(), target, mask))
                    return true;
            }

            // The adapter list can miss the address the system actually uses (some Wi-Fi drivers).
            var localAddress = GetLocalLanAddress();
            if (localAddress != null && SameSubnet(localAddress.GetAddressBytes(), target, homeMask))
                return true;
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not check local network: {ex.Message}");
        }

        return false;
    }

    private static bool SameSubnet(byte[] local, byte[] target, byte[] mask)
    {
        if (local.Length != 4 || target.Length != 4 || mask.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if ((local[i] & mask[i]) != (target[i] & mask[i]))
                return false;
        }

        return true;
    }

    internal static IPAddress? GetLocalLanAddress()
    {
        // Ask the system which address it would use to reach the internet. That is the address this
        // machine has on the router's network, whatever range the router hands out (some home
        // routers use ranges outside the usual 192.168.x.x / 10.x.x.x). Nothing is sent.
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(new IPAddress(new byte[] { 1, 1, 1, 1 }), 9));

            if (socket.LocalEndPoint is IPEndPoint local && IsLanCandidate(local.Address))
                return local.Address;
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not ask the system for a LAN address: {ex.Message}");
        }

        try
        {
            // Otherwise prefer the adapter that has a default gateway (the one actually connected to the router).
            IPAddress? fallback = null;

            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsUsableAdapter(adapter))
                    continue;

                var properties = adapter.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

                foreach (var unicast in properties.UnicastAddresses)
                {
                    if (!IsLanCandidate(unicast.Address))
                        continue;

                    if (hasGateway)
                        return unicast.Address;

                    if (IsPrivate(unicast.Address))
                        fallback ??= unicast.Address;
                }
            }

            return fallback;
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Could not find a LAN address: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<UnicastIPAddressInformation> GetUsableIpv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsUsableAdapter)
            .SelectMany(a => a.GetIPProperties().UnicastAddresses)
            .Where(u => IsLanCandidate(u.Address));

    private static bool IsUsableAdapter(NetworkInterface adapter) =>
        adapter.OperationalStatus == OperationalStatus.Up
        && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback
        && adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel;

    /// <summary>
    /// An IPv4 address that could be this machine's address on a home network: not loopback,
    /// not "no address", and not the 169.254.x.x range Windows uses when it got no address at all.
    /// </summary>
    private static bool IsLanCandidate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var b = address.GetAddressBytes();
        return b[0] != 0 && b[0] != 127 && !(b[0] == 169 && b[1] == 254) && b[0] < 224;
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168);
    }

    private static bool TryDecodeBytes(string code, int expectedBytes, out byte[] decoded, out string error)
    {
        error = string.Empty;
        decoded = Array.Empty<byte>();

        try
        {
            var raw = Base58.Decode(code);
            if (raw.Length > expectedBytes)
            {
                var extra = raw.Length - expectedBytes;
                for (var i = 0; i < extra; i++)
                {
                    if (raw[i] != 0)
                    {
                        error = "Join code decoded to unexpected length.";
                        return false;
                    }
                }
            }

            decoded = new byte[expectedBytes];
            var count = Math.Min(raw.Length, expectedBytes);
            Buffer.BlockCopy(raw, raw.Length - count, decoded, expectedBytes - count, count);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Join code is invalid: {ex.Message}";
            return false;
        }
    }

    public static string Encode(IPAddress address, ushort port)
    {
        var bytes = address.GetAddressBytes();
        // I honestly don't think an ipv6 address can even make it here to be honest
        // But if it does somehow! we can support it!
        if (bytes.Length == 4)
            return EncodeIPv4(bytes, port);
        if (bytes.Length == 16)
            return EncodeIPv6(bytes, port);

        throw new ArgumentException("Only IPv4 and IPv6 addresses are supported.", nameof(address));
    }

    public static bool TryDecode(string code, out IPAddress address, out ushort port, out string error)
    {
        address = null!;
        port = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            error = "Join code is empty.";
            return false;
        }

        code = code.Trim();

        int expectedBytes;
        if (code.Length == Ipv4CodeLength)
        {
            expectedBytes = Ipv4Bytes;
        }
        else if (code.Length == Ipv6CodeLength)
        {
            expectedBytes = Ipv6Bytes;
        }
        else
        {
            error = "Invalid join code length.";
            return false;
        }

        try
        {
            var decoded = Base58.Decode(code);
            if (decoded.Length > expectedBytes)
            {
                int extra = decoded.Length - expectedBytes;
                bool leadingZeros = true;
                for (int i = 0; i < extra; i++)
                {
                    if (decoded[i] != 0)
                    {
                        leadingZeros = false;
                        break;
                    }
                }

                if (!leadingZeros)
                {
                    error = "Join code decoded to unexpected length.";
                    return false;
                }

                var trimmed = new byte[expectedBytes];
                Buffer.BlockCopy(decoded, extra, trimmed, 0, expectedBytes);
                decoded = trimmed;
            }

            if (decoded.Length < expectedBytes)
            {
                var padded = new byte[expectedBytes];
                Buffer.BlockCopy(decoded, 0, padded, expectedBytes - decoded.Length, decoded.Length);
                decoded = padded;
            }

            var ipBytes = new byte[expectedBytes - 2];
            Buffer.BlockCopy(decoded, 0, ipBytes, 0, ipBytes.Length);
            port = (ushort)((decoded[expectedBytes - 2] << 8) | decoded[expectedBytes - 1]);

            if (port == 0)
            {
                error = "Join code contains an invalid port.";
                return false;
            }

            address = new IPAddress(ipBytes);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Join code is invalid: {ex.Message}";
            return false;
        }
    }

    private static string EncodeIPv4(byte[] addressBytes, ushort port)
    {
        var bytes = new byte[Ipv4Bytes];
        Buffer.BlockCopy(addressBytes, 0, bytes, 0, 4);
        bytes[4] = (byte)(port >> 8);
        bytes[5] = (byte)port;

        var encoded = Base58.Encode(bytes);
        return encoded.PadLeft(Ipv4CodeLength, '1');
    }

    private static string EncodeIPv6(byte[] addressBytes, ushort port)
    {
        var bytes = new byte[Ipv6Bytes];
        Buffer.BlockCopy(addressBytes, 0, bytes, 0, 16);
        bytes[16] = (byte)(port >> 8);
        bytes[17] = (byte)port;

        var encoded = Base58.Encode(bytes);
        return encoded.PadLeft(Ipv6CodeLength, '1');
    }
}
