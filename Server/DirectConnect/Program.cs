using System.Net;
using System.Security.Cryptography;
using System.Text;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Network;

DirectConnectOptions options;
try
{
    options = DirectConnectOptions.Parse(args);
}
catch (Exception exception) when (exception is ArgumentException or FormatException)
{
    Console.Error.WriteLine($"UltraSim Direct Connect configuration error: {exception.Message}");
    return 2;
}

if (options.EphemeralSmoke)
{
    using NetworkServerIdentity smokeIdentity = NetworkServerIdentity.CreateEphemeral();
    using HostedUdpAdmissionHost smokeHost = CreateHost(
        new IPEndPoint(IPAddress.Loopback, 0), smokeIdentity,
        new ApplicationRealmId(0xF03A, 0x534D4F4B45));
    smokeHost.Pump();
    Console.WriteLine(
        $"F0.3b DIRECT CONNECT PLAYABLE SERVER SMOKE PASS endpoint={smokeHost.LocalEndPoint} " +
        $"protocol={UltraSimGameProtocol.Manifest.Version}/" +
        $"{UltraSimGameProtocol.Manifest.Fingerprint:x16}");
    return 0;
}

Directory.CreateDirectory(options.IdentityDirectory);
using NetworkServerIdentity identity = LoadOrCreateIdentity(options.IdentityDirectory);
ApplicationRealmId realm = LoadOrCreateRealm(options.IdentityDirectory);
using HostedUdpAdmissionHost host = CreateHost(options.BindEndPoint, identity, realm);
string fingerprint = Fingerprint(identity.PublicKey);
Console.WriteLine("UltraSim Direct Connect Concept Demo server ready.");
Console.WriteLine($"Endpoint: {host.LocalEndPoint}");
Console.WriteLine($"Application realm: {realm}");
Console.WriteLine($"Deployment key SHA-256: {Group(fingerprint)}");
Console.WriteLine("This F0.3b endpoint admits one authenticated remote guest into the Concept Demo.");
Console.WriteLine("Press Ctrl+C to stop.");

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};
while (!stopping.IsCancellationRequested)
{
    host.Pump();
    Thread.Sleep(1);
}
return 0;

static HostedUdpAdmissionHost CreateHost(
    IPEndPoint endpoint,
    NetworkServerIdentity identity,
    ApplicationRealmId realm)
{
    UdpProtocolLimits udp = UdpProtocolLimits.Default;
    SharedUdpProtocolLimits shared = SharedUdpProtocolLimits.Default with
    {
        // F0.3b proves one external remote participant against one owner-thread Game runtime.
        // A shared multi-participant authority is a separate, explicit later checkpoint.
        MaximumSessions = 1,
    };
    HostedUdpOwnerShardingLimits owners = HostedUdpOwnerShardingLimits.Default with
    {
        MaximumOwners = 1,
        MaximumConnections = 1,
    };
    return new HostedUdpAdmissionHost(
        endpoint,
        identity,
        NetworkProtocolLimits.Default,
        udp,
        shared,
        HostedUdpAdmissionLimits.Default,
        owners,
        new RemoteGuestPlayableApplicationFactory(realm),
        UltraSimGameProtocol.Manifest);
}

static NetworkServerIdentity LoadOrCreateIdentity(string directory)
{
    string path = Path.Combine(directory, "deployment-key.pkcs8");
    if (File.Exists(path))
    {
        byte[] stored = File.ReadAllBytes(path);
        try
        {
            if (stored.Length > 4_096 ||
                !NetworkServerIdentity.TryCreateFromPkcs8(stored, out NetworkServerIdentity? restored))
                throw new InvalidDataException(
                    $"Deployment identity '{path}' is not one canonical P-256 PKCS#8 key.");
            return restored!;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stored);
        }
    }
    using NetworkServerIdentity created = NetworkServerIdentity.CreateEphemeral();
    byte[] key = created.ExportPkcs8PrivateKey();
    try
    {
        WriteNewSecret(path, key);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(key);
    }
    byte[] persisted = File.ReadAllBytes(path);
    try
    {
        if (!NetworkServerIdentity.TryCreateFromPkcs8(
                persisted, out NetworkServerIdentity? identity))
            throw new InvalidDataException("The newly persisted deployment identity could not be reopened.");
        return identity!;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(persisted);
    }
}

static ApplicationRealmId LoadOrCreateRealm(string directory)
{
    string path = Path.Combine(directory, "application-realm.txt");
    if (File.Exists(path))
    {
        string value = File.ReadAllText(path, new UTF8Encoding(false, true)).TrimEnd('\r', '\n');
        if (!ApplicationRealmId.TryParse(value, out ApplicationRealmId realm) ||
            !string.Equals(value, realm.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Application realm '{path}' is not one canonical 128-bit realm ID.");
        return realm;
    }
    Span<byte> random = stackalloc byte[16];
    RandomNumberGenerator.Fill(random);
    ulong high = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(random);
    ulong low = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(random[8..]);
    if (high == 0 && low == 0) low = 1;
    var created = new ApplicationRealmId(high, low);
    WriteNewSecret(path, Encoding.ASCII.GetBytes(created + Environment.NewLine));
    return created;
}

static void WriteNewSecret(string path, ReadOnlySpan<byte> bytes)
{
    string temporary = path + $".{Environment.ProcessId}.tmp";
    try
    {
        using (var stream = new FileStream(
                   temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(bytes);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, overwrite: false);
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }
}

static string Fingerprint(ReadOnlySpan<byte> key)
{
    Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
    SHA256.HashData(key, hash);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static string Group(string fingerprint) => string.Join(':',
    Enumerable.Range(0, fingerprint.Length / 2)
        .Select(index => fingerprint.Substring(index * 2, 2)));

internal readonly record struct DirectConnectOptions(
    IPEndPoint BindEndPoint,
    string IdentityDirectory,
    bool EphemeralSmoke)
{
    internal static DirectConnectOptions Parse(string[] arguments)
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 7777);
        string identity = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UltraSim", "Server", "DirectConnect");
        bool smoke = false;
        foreach (string argument in arguments)
        {
            if (argument == "--ephemeral-smoke")
            {
                smoke = true;
                continue;
            }
            if (argument.StartsWith("--bind=", StringComparison.Ordinal))
            {
                string value = argument["--bind=".Length..];
                int separator = value.LastIndexOf(':');
                if (separator <= 0 ||
                    !IPAddress.TryParse(value.AsSpan(0, separator), out IPAddress? address) ||
                    address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                    !int.TryParse(value.AsSpan(separator + 1), out int port) ||
                    port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
                    throw new ArgumentException("--bind must be one IPv4 address and port.");
                endpoint = new IPEndPoint(address, port);
                continue;
            }
            if (argument.StartsWith("--identity-dir=", StringComparison.Ordinal))
            {
                string value = argument["--identity-dir=".Length..];
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("--identity-dir cannot be empty.");
                identity = Path.GetFullPath(value);
                continue;
            }
            throw new ArgumentException($"Unknown option '{argument}'.");
        }
        return new DirectConnectOptions(endpoint, identity, smoke);
    }
}
