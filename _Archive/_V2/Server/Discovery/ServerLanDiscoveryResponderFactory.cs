using System.Net;
using UltraSim.Discovery;

namespace UltraSim.Server.Discovery;

public static class ServerLanDiscoveryResponderFactory
{
    public static ServerLanDiscoveryResponderHost Create(
        ServerDiscoveryPublicationSettings settings,
        Func<ServerStatusSnapshot> statusSnapshotFactory,
        IPEndPoint? listenEndPoint = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(statusSnapshotFactory);

        ServerDiscoveryAdvertisedSettings advertisedSettings =
            ServerDiscoveryPublicationFactory.BuildAdvertisedSettings(settings);

        return new ServerLanDiscoveryResponderHost(
            () => ServerDiscoveryContractBuilder.BuildRow(
                advertisedSettings,
                statusSnapshotFactory(),
                providerId: "lan",
                providerKind: ServerDiscoveryProviderKind.Lan),
            listenEndPoint);
    }

    public static ServerLanDiscoveryResponderHost Create(
        ServerDiscoveryPublicationSettings settings,
        Func<ServerStatusSnapshot> statusSnapshotFactory,
        string bindAddress,
        int port)
    {
        return Create(
            settings,
            statusSnapshotFactory,
            new IPEndPoint(ParseBindAddress(bindAddress), NormalizePort(port)));
    }

    private static int NormalizePort(int port)
    {
        if ((uint)port > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(port), "LAN discovery port must be between 0 and 65535.");

        return port;
    }

    private static IPAddress ParseBindAddress(string bindAddress)
    {
        string value = bindAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "any", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Any;
        }

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(value, out IPAddress? parsed))
            return parsed;

        throw new ArgumentException($"LAN discovery bind address '{bindAddress}' is not a valid IP address.", nameof(bindAddress));
    }
}
