using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Network;
using UltraSim.Game.UI;
using UltraSim.UI;
using UltraSim.Server.ConceptDemo;

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
    using SharedPlayableAuthority smokeAuthority =
        SharedPlayableAuthority.CreateConceptDemo();
    using HostedUdpAdmissionHost smokeHost = CreateHost(
        new IPEndPoint(IPAddress.Loopback, 0), smokeIdentity,
        new ApplicationRealmId(0xF03A, 0x534D4F4B45), smokeAuthority);
    smokeHost.Pump();
    _ = smokeAuthority.TickDue();
    Console.WriteLine(
        $"F0.3b DIRECT CONNECT PLAYABLE SERVER SMOKE PASS endpoint={smokeHost.LocalEndPoint} " +
        $"protocol={UltraSimGameProtocol.Manifest.Version}/" +
        $"{UltraSimGameProtocol.Manifest.Fingerprint:x16}");
    return 0;
}

Directory.CreateDirectory(options.IdentityDirectory);
using NetworkServerIdentity identity = LoadOrCreateIdentity(options.IdentityDirectory);
ApplicationRealmId realm = LoadOrCreateRealm(options.IdentityDirectory);
using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo();
using ConceptDemoContentDeployment content = ConceptDemoContentDeployment.Create(
    realm, options.ContentEndPoint);
RemoteClientProfileAdvertisement clientProfile =
    ConceptDemoClientProfiles.Create(options.ClientProfile);
using HostedUdpAdmissionHost host = CreateHost(
    options.BindEndPoint, identity, realm, authority, content.Authorization,
    [UiDefinitionIdentity.From(InventoryUi.Definition)], clientProfile);
string fingerprint = Fingerprint(identity.PublicKey);
Console.WriteLine("UltraSim Direct Connect Concept Demo server ready.");
Console.WriteLine($"Endpoint: {host.LocalEndPoint}");
Console.WriteLine($"Application realm: {realm}");
Console.WriteLine($"Deployment key SHA-256: {Group(fingerprint)}");
Console.WriteLine($"Content archive: {content.Snapshot.ArchiveUri} ({content.Snapshot.ArchiveBytes} bytes)");
Console.WriteLine(
    $"WorldPack: {content.Snapshot.WorldPackFingerprint} " +
    $"({content.Snapshot.WorldChunks} chunks / {content.Snapshot.WorldPackBytes} bytes)");
Console.WriteLine(
    $"Client profile: {options.ClientProfile} revision={clientProfile.Revision} " +
    $"hash={ProfileHash(clientProfile.Hash)}");
Console.WriteLine("This M0.0 endpoint admits up to eight independent guests into one shared Concept Demo authority.");
Console.WriteLine("Press Ctrl+C to stop.");
WriteReady(options.ReadyFile, new DirectConnectReady(
    host.LocalEndPoint.ToString(), realm.ToString(), fingerprint,
    content.Snapshot.ArchiveUri.ToString(), content.Snapshot.ArchiveBytes,
    content.Snapshot.WorldPackBytes, content.Snapshot.UiBytes,
    content.Snapshot.ManifestHash.ToString(), content.Snapshot.PlanHash.ToString(),
    content.Snapshot.CatalogFingerprint.ToString(),
    content.Snapshot.WorldPackFingerprint.ToString(), content.Snapshot.WorldChunks,
    options.ClientProfile, clientProfile.Revision, ProfileHash(clientProfile.Hash)));

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};
while (!stopping.IsCancellationRequested)
{
    host.Pump();
    _ = authority.TickDue();
    Thread.Sleep(1);
}
return 0;

static HostedUdpAdmissionHost CreateHost(
    IPEndPoint endpoint,
    NetworkServerIdentity identity,
    ApplicationRealmId realm,
    SharedPlayableAuthority authority,
    RemoteContentAuthorization? contentAuthorization = null,
    IEnumerable<UiDefinitionIdentity>? contentBackedUiDefinitions = null,
    RemoteClientProfileAdvertisement? clientProfile = null)
{
    UdpProtocolLimits udp = UdpProtocolLimits.Default;
    SharedUdpProtocolLimits shared = SharedUdpProtocolLimits.Default with
    {
        MaximumSessions = 8,
    };
    var applications = new SharedRemoteGuestPlayableApplicationFactory(
        authority, realm, contentAuthorization, contentBackedUiDefinitions, clientProfile);
    return new HostedUdpAdmissionHost(
        endpoint,
        identity,
        NetworkProtocolLimits.Default,
        udp,
        shared,
        HostedUdpAdmissionLimits.Default,
        applications,
        UltraSimGameProtocol.Manifest);
}

static void WriteReady(string? path, DirectConnectReady ready)
{
    if (string.IsNullOrEmpty(path)) return;
    string full = Path.GetFullPath(path);
    string? directory = Path.GetDirectoryName(full);
    if (string.IsNullOrEmpty(directory))
        throw new InvalidOperationException("The ready-file path has no parent directory.");
    Directory.CreateDirectory(directory);
    string temporary = full + $".{Environment.ProcessId}.tmp";
    try
    {
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(ready) + Environment.NewLine,
            new UTF8Encoding(false, true));
        File.Move(temporary, full, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }
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

static string ProfileHash(RemoteClientProfileHash hash)
{
    Span<byte> bytes = stackalloc byte[32];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes, hash.Part0);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], hash.Part1);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], hash.Part2);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], hash.Part3);
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

internal readonly record struct DirectConnectOptions(
    IPEndPoint BindEndPoint,
    IPEndPoint ContentEndPoint,
    string IdentityDirectory,
    string? ReadyFile,
    string ClientProfile,
    bool EphemeralSmoke)
{
    internal static DirectConnectOptions Parse(string[] arguments)
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 7777);
        var contentEndpoint = new IPEndPoint(IPAddress.Loopback, 7778);
        string identity = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UltraSim", "Server", "DirectConnect");
        bool smoke = false;
        string? readyFile = null;
        string clientProfile = "third-person";
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
            if (argument.StartsWith("--content-bind=", StringComparison.Ordinal))
            {
                contentEndpoint = ParseEndpoint(
                    argument["--content-bind=".Length..], "--content-bind");
                continue;
            }
            if (argument.StartsWith("--ready-file=", StringComparison.Ordinal))
            {
                string value = argument["--ready-file=".Length..];
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("--ready-file cannot be empty.");
                readyFile = Path.GetFullPath(value);
                continue;
            }
            if (argument.StartsWith("--client-profile=", StringComparison.Ordinal))
            {
                clientProfile = argument["--client-profile=".Length..].Trim().ToLowerInvariant();
                if (clientProfile is not ("orbit" or "third-person" or "independent"))
                    throw new ArgumentException(
                        "--client-profile must be orbit, third-person, or independent.");
                continue;
            }
            throw new ArgumentException($"Unknown option '{argument}'.");
        }
        return new DirectConnectOptions(
            endpoint, contentEndpoint, identity, readyFile, clientProfile, smoke);

        static IPEndPoint ParseEndpoint(string value, string option)
        {
            int separator = value.LastIndexOf(':');
            if (separator <= 0 ||
                !IPAddress.TryParse(value.AsSpan(0, separator), out IPAddress? address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                !int.TryParse(value.AsSpan(separator + 1), out int port) ||
                port is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
                throw new ArgumentException($"{option} must be one IPv4 address and nonzero port.");
            return new IPEndPoint(address, port);
        }
    }
}

internal readonly record struct DirectConnectReady(
    string Endpoint,
    string Realm,
    string Fingerprint,
    string ContentUri,
    int ArchiveBytes,
    int WorldPackBytes,
    int UiBytes,
    string ManifestHash,
    string PlanHash,
    string CatalogFingerprint,
    string WorldPackFingerprint,
    int WorldChunks,
    string ClientProfile,
    ulong ClientProfileRevision,
    string ClientProfileHash);
