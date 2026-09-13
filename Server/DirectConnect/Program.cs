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
using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo(
    new SharedPlayableAuthorityLimits(options.MaximumParticipants),
    options.SimulationFrequencyHz);
using ConceptDemoContentDeployment content = ConceptDemoContentDeployment.Create(
    realm, options.ContentEndPoint);
RemoteClientProfileAdvertisement clientProfile =
    ConceptDemoClientProfiles.Create(options.ClientProfile);
RemoteServerPerformanceProfile performanceProfile =
    ConceptDemoServerPerformanceProfiles.Create(
        options.SimulationFrequencyHz,
        options.MaximumInputHz,
    options.RecommendedInputHz,
    options.InputBurstCapacity,
    options.InputBurstWindowMilliseconds,
    checked((options.NetworkServiceDelayMilliseconds + 1) * 1_000));
using HostedUdpAdmissionHost host = CreateHost(
    options.BindEndPoint, identity, realm, authority, content.Authorization,
    [
        UiDefinitionIdentity.From(InventoryUi.Definition),
        UiDefinitionIdentity.From(ServerMonitorUi.Definition),
    ], clientProfile, performanceProfile,
    options.MaximumParticipants, options.AdmissionBurstPerAddress,
    options.PublicationPolicy);
host.ConfigureTelemetry(options.TelemetryLevel);
authority.ConfigureTelemetry(options.TelemetryLevel == NetworkTelemetryLevel.Off
    ? GameTelemetryLevel.Off
    : options.TelemetryLevel == NetworkTelemetryLevel.Detailed
        ? GameTelemetryLevel.Detailed
        : GameTelemetryLevel.Basic);
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
    $"Actor presentation: {content.Snapshot.ActorPresentationBytes} verified GLB bytes " +
    "(X/Y Add + X Replace)");
Console.WriteLine(
    $"Client profile: {options.ClientProfile} revision={clientProfile.Revision} " +
    $"hash={ProfileHash(clientProfile.Hash)}");
Console.WriteLine(
    $"Server performance: authority={performanceProfile.SimulationFrequencyHz}Hz " +
    $"network-max-service={performanceProfile.MaximumNetworkServiceIntervalMicroseconds}us " +
    $"input-max={performanceProfile.MaximumInputHz}Hz " +
    $"input-recommended={performanceProfile.RecommendedInputHz}Hz " +
    $"burst={performanceProfile.InputBurstCapacity}/" +
    $"{performanceProfile.InputBurstWindowMilliseconds}ms " +
    $"revision={performanceProfile.Revision} hash={PerformanceHash(performanceProfile.Hash)}");
Console.WriteLine(
    $"Public ChunkPublication: representation={options.PublicationPolicy.Representation} " +
    $"cadence={options.PublicationPolicy.Cadence.ImmediateSteps}/" +
    $"{options.PublicationPolicy.Cadence.NearSteps}/" +
    $"{options.PublicationPolicy.Cadence.FarSteps}");
Console.WriteLine(
    $"This shared endpoint admits up to {options.MaximumParticipants} independent guests " +
    "into one Concept Demo authority.");
Console.WriteLine("Press Ctrl+C to stop.");
WriteReady(options.ReadyFile, new DirectConnectReady(
    host.LocalEndPoint.ToString(), realm.ToString(), fingerprint,
    content.Snapshot.ArchiveUri.ToString(), content.Snapshot.ArchiveBytes,
    content.Snapshot.WorldPackBytes, content.Snapshot.UiBytes,
    content.Snapshot.ActorPresentationBytes,
    content.Snapshot.ManifestHash.ToString(), content.Snapshot.PlanHash.ToString(),
    content.Snapshot.CatalogFingerprint.ToString(),
    content.Snapshot.WorldPackFingerprint.ToString(), content.Snapshot.WorldChunks,
    options.ClientProfile, clientProfile.Revision, ProfileHash(clientProfile.Hash),
    performanceProfile.Revision, PerformanceHash(performanceProfile.Hash),
    performanceProfile.SimulationFrequencyHz,
    performanceProfile.MaximumNetworkServiceIntervalMicroseconds,
    performanceProfile.MaximumInputHz, performanceProfile.RecommendedInputHz,
    performanceProfile.InputBurstCapacity,
    performanceProfile.InputBurstWindowMilliseconds,
    options.MaximumParticipants, authority.GetSnapshot().GenerationId,
    options.PublicationPolicy.Representation.ToString(),
    options.PublicationPolicy.Cadence.ImmediateSteps,
    options.PublicationPolicy.Cadence.NearSteps,
    options.PublicationPolicy.Cadence.FarSteps));

using var stopping = new CancellationTokenSource();
DirectConnectWireAccounting? wireAccounting = null;
bool wireAccountingWritten = false;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};
while (!stopping.IsCancellationRequested)
{
    if (options.StopFile is not null && File.Exists(options.StopFile))
        break;
    host.Pump();
    _ = authority.TickDue();
    if (wireAccounting is null && options.AccountingStartFile is not null &&
        File.Exists(options.AccountingStartFile))
    {
        wireAccounting = new DirectConnectWireAccounting(host, authority, content);
        WriteReady(options.AccountingReadyFile, new { Ready = true });
        Console.WriteLine("PF2 Direct Connect wire-accounting window started.");
    }
    wireAccounting?.Sample();
    if (!wireAccountingWritten && wireAccounting is not null &&
        options.AccountingStopFile is not null && File.Exists(options.AccountingStopFile))
    {
        WriteReady(options.AccountingSummaryFile,
            wireAccounting.Complete(host, authority, content));
        wireAccountingWritten = true;
        Console.WriteLine("PF2 Direct Connect wire-accounting window completed.");
    }
    if (options.NetworkServiceDelayMilliseconds != 0)
        Thread.Sleep(options.NetworkServiceDelayMilliseconds);
    else
        Thread.Yield();
}
long retirementDeadline = Environment.TickCount64 + 10_000;
while ((authority.GetSnapshot().ParticipantCount != 0 ||
        host.GetSnapshot().EstablishedSessions != 0) &&
       Environment.TickCount64 < retirementDeadline)
{
    host.Pump();
    _ = authority.TickDue();
    Thread.Sleep(1);
}
SharedPlayableAuthoritySnapshot finalAuthority = authority.GetSnapshot();
HostedUdpAdmissionSnapshot finalHost = host.GetSnapshot();
if (!wireAccountingWritten && wireAccounting is not null)
{
    WriteReady(options.AccountingSummaryFile,
        wireAccounting.Complete(host, authority, content));
    wireAccountingWritten = true;
}
bool cleanRetirement = finalAuthority.ParticipantCount == 0 &&
    finalHost.EstablishedSessions == 0;
if (options.SummaryFile is not null)
    WriteReady(options.SummaryFile, new DirectConnectSummary(
        finalAuthority.GenerationId,
        finalAuthority.SimulationTick,
        finalAuthority.Joins,
        finalAuthority.Leaves,
        finalAuthority.ParticipantCount,
        finalHost.EstablishedSessions,
        cleanRetirement));
Console.WriteLine(
    $"UltraSim Direct Connect stopped generation={finalAuthority.GenerationId} " +
    $"participants={finalAuthority.ParticipantCount} sessions={finalHost.EstablishedSessions} " +
    $"joins={finalAuthority.Joins} leaves={finalAuthority.Leaves} clean={cleanRetirement}.");
return cleanRetirement ? 0 : 3;

static HostedUdpAdmissionHost CreateHost(
    IPEndPoint endpoint,
    NetworkServerIdentity identity,
    ApplicationRealmId realm,
    SharedPlayableAuthority authority,
    RemoteContentAuthorization? contentAuthorization = null,
    IEnumerable<UiDefinitionIdentity>? contentBackedUiDefinitions = null,
    RemoteClientProfileAdvertisement? clientProfile = null,
    RemoteServerPerformanceProfile? serverPerformanceProfile = null,
    int maximumParticipants = 8,
    int admissionBurstPerAddress = 4,
    RemoteChunkPublicationPolicy publicationPolicy = default)
{
    if (publicationPolicy == default)
        publicationPolicy = RemoteChunkPublicationPolicy.CurrentBundled;
    publicationPolicy.Validate();
    UdpProtocolLimits udp = UdpProtocolLimits.Default;
    SharedUdpProtocolLimits shared = SharedUdpProtocolLimits.Default with
    {
        MaximumSessions = maximumParticipants,
    };
    var applications = new SharedRemoteGuestPlayableApplicationFactory(
        authority, realm, contentAuthorization, contentBackedUiDefinitions, clientProfile,
        serverPerformanceProfile: serverPerformanceProfile,
        publicationPolicy: publicationPolicy);
    return new HostedUdpAdmissionHost(
        endpoint,
        identity,
        NetworkProtocolLimits.Default,
        udp,
        shared,
        HostedUdpAdmissionLimits.Default with
        {
            MaximumValidatedCandidates = Math.Max(
                HostedUdpAdmissionLimits.Default.MaximumValidatedCandidates,
                maximumParticipants),
            MaximumChallengesPerAddressPerBucket = Math.Max(
                HostedUdpAdmissionLimits.Default.MaximumChallengesPerAddressPerBucket,
                admissionBurstPerAddress),
            MaximumValidationsPerAddressPerBucket = admissionBurstPerAddress,
        },
        applications,
        UltraSimGameProtocol.Manifest);
}

static void WriteReady<T>(string? path, T ready)
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

static string PerformanceHash(RemoteServerPerformanceProfileHash hash)
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
    string? StopFile,
    string? SummaryFile,
    string? AccountingStartFile,
    string? AccountingReadyFile,
    string? AccountingStopFile,
    string? AccountingSummaryFile,
    string ClientProfile,
    int SimulationFrequencyHz,
    int MaximumInputHz,
    int RecommendedInputHz,
    int InputBurstCapacity,
    int InputBurstWindowMilliseconds,
    int NetworkServiceDelayMilliseconds,
    bool EphemeralSmoke,
    int MaximumParticipants,
    int AdmissionBurstPerAddress,
    NetworkTelemetryLevel TelemetryLevel,
    RemoteChunkPublicationPolicy PublicationPolicy)
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
        string? stopFile = null;
        string? summaryFile = null;
        string? accountingStartFile = null;
        string? accountingReadyFile = null;
        string? accountingStopFile = null;
        string? accountingSummaryFile = null;
        string clientProfile = "third-person";
        int simulationFrequencyHz = 60;
        int maximumInputHz = 60;
        int recommendedInputHz = 30;
        int inputBurstCapacity = 10;
        int inputBurstWindowMilliseconds = 100;
        int networkServiceDelayMilliseconds = 1;
        int maximumParticipants = 8;
        int admissionBurstPerAddress =
            HostedUdpAdmissionLimits.Default.MaximumValidationsPerAddressPerBucket;
        NetworkTelemetryLevel telemetryLevel = NetworkTelemetryLevel.Off;
        RemoteChunkPublicationRepresentation publicationRepresentation =
            RemoteChunkPublicationRepresentation.Message129WhenBeneficial;
        OfflineDirectChunkCadence publicationCadence = OfflineDirectChunkCadence.EvidenceProfile;
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
            if (argument.StartsWith("--stop-file=", StringComparison.Ordinal))
            {
                string value = argument["--stop-file=".Length..];
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("--stop-file cannot be empty.");
                stopFile = Path.GetFullPath(value);
                continue;
            }
            if (argument.StartsWith("--summary-file=", StringComparison.Ordinal))
            {
                string value = argument["--summary-file=".Length..];
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("--summary-file cannot be empty.");
                summaryFile = Path.GetFullPath(value);
                continue;
            }
            if (argument.StartsWith("--accounting-start-file=", StringComparison.Ordinal))
            {
                accountingStartFile = RequiredPath(argument, "--accounting-start-file=");
                continue;
            }
            if (argument.StartsWith("--accounting-stop-file=", StringComparison.Ordinal))
            {
                accountingStopFile = RequiredPath(argument, "--accounting-stop-file=");
                continue;
            }
            if (argument.StartsWith("--accounting-ready-file=", StringComparison.Ordinal))
            {
                accountingReadyFile = RequiredPath(argument, "--accounting-ready-file=");
                continue;
            }
            if (argument.StartsWith("--accounting-summary-file=", StringComparison.Ordinal))
            {
                accountingSummaryFile = RequiredPath(argument, "--accounting-summary-file=");
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
            if (argument.StartsWith("--maximum-participants=", StringComparison.Ordinal))
            {
                string value = argument["--maximum-participants=".Length..];
                if (!int.TryParse(value, out maximumParticipants) ||
                    maximumParticipants is <= 0 or > 64)
                    throw new ArgumentException(
                        "--maximum-participants must be between 1 and 64.");
                continue;
            }
            if (argument.StartsWith("--authority-hz=", StringComparison.Ordinal))
            {
                string value = argument["--authority-hz=".Length..];
                if (!int.TryParse(value, out simulationFrequencyHz) ||
                    simulationFrequencyHz is not (20 or 30 or 60))
                    throw new ArgumentException("--authority-hz must be 20, 30, or 60.");
                continue;
            }
            if (argument.StartsWith("--maximum-input-hz=", StringComparison.Ordinal))
            {
                string value = argument["--maximum-input-hz=".Length..];
                if (!int.TryParse(value, out maximumInputHz) || maximumInputHz is <= 0 or > 240)
                    throw new ArgumentException("--maximum-input-hz must be between 1 and 240.");
                continue;
            }
            if (argument.StartsWith("--recommended-input-hz=", StringComparison.Ordinal))
            {
                string value = argument["--recommended-input-hz=".Length..];
                if (!int.TryParse(value, out recommendedInputHz) ||
                    recommendedInputHz is <= 0 or > 240)
                    throw new ArgumentException(
                        "--recommended-input-hz must be between 1 and 240.");
                continue;
            }
            if (argument.StartsWith("--input-burst=", StringComparison.Ordinal))
            {
                string value = argument["--input-burst=".Length..];
                if (!int.TryParse(value, out inputBurstCapacity) ||
                    inputBurstCapacity is <= 0 or > 32)
                    throw new ArgumentException("--input-burst must be between 1 and 32.");
                continue;
            }
            if (argument.StartsWith("--input-burst-window-ms=", StringComparison.Ordinal))
            {
                string value = argument["--input-burst-window-ms=".Length..];
                if (!int.TryParse(value, out inputBurstWindowMilliseconds) ||
                    inputBurstWindowMilliseconds is < 10 or > 5_000)
                    throw new ArgumentException(
                        "--input-burst-window-ms must be between 10 and 5000.");
                continue;
            }
            if (argument.StartsWith("--network-service-delay-ms=", StringComparison.Ordinal))
            {
                string value = argument["--network-service-delay-ms=".Length..];
                if (!int.TryParse(value, out networkServiceDelayMilliseconds) ||
                    networkServiceDelayMilliseconds is < 0 or > 19)
                    throw new ArgumentException(
                        "--network-service-delay-ms must be between 0 and 19.");
                continue;
            }
            if (argument.StartsWith("--same-address-admission-burst=", StringComparison.Ordinal))
            {
                string value = argument["--same-address-admission-burst=".Length..];
                if (!int.TryParse(value, out admissionBurstPerAddress) ||
                    admissionBurstPerAddress is <= 0 or > 4_096)
                    throw new ArgumentException(
                        "--same-address-admission-burst must be between 1 and 4096.");
                continue;
            }
            if (argument.StartsWith("--telemetry=", StringComparison.Ordinal))
            {
                string value = argument["--telemetry=".Length..];
                if (!Enum.TryParse(value, ignoreCase: true, out telemetryLevel))
                    throw new ArgumentException("--telemetry must be Off, Basic, or Detailed.");
                continue;
            }
            if (argument.StartsWith("--public-representation=", StringComparison.Ordinal))
            {
                string value = argument["--public-representation=".Length..]
                    .Trim().ToLowerInvariant();
                publicationRepresentation = value switch
                {
                    "standalone" => RemoteChunkPublicationRepresentation.Standalone,
                    "message129" or "bundle" =>
                        RemoteChunkPublicationRepresentation.Message129WhenBeneficial,
                    _ => throw new ArgumentException(
                        "--public-representation must be standalone or message129."),
                };
                continue;
            }
            if (argument.StartsWith("--public-cadence=", StringComparison.Ordinal))
            {
                string[] values = argument["--public-cadence=".Length..].Split('/');
                if (values.Length != 3 ||
                    !uint.TryParse(values[0], out uint immediate) ||
                    !uint.TryParse(values[1], out uint near) ||
                    !uint.TryParse(values[2], out uint far))
                    throw new ArgumentException(
                        "--public-cadence must be Immediate/Near/Far positive step counts.");
                publicationCadence = new OfflineDirectChunkCadence(immediate, near, far);
                if (!publicationCadence.IsValid)
                    throw new ArgumentException(
                        "--public-cadence must be positive and ordered Immediate <= Near <= Far.");
                continue;
            }
            throw new ArgumentException($"Unknown option '{argument}'.");
        }
        bool anyAccounting = accountingStartFile is not null || accountingReadyFile is not null ||
            accountingStopFile is not null || accountingSummaryFile is not null;
        if (anyAccounting && (accountingStartFile is null || accountingReadyFile is null ||
            accountingStopFile is null || accountingSummaryFile is null))
            throw new ArgumentException(
                "PF2 accounting start, stop, and summary files must be supplied together.");
        if (recommendedInputHz > maximumInputHz)
            throw new ArgumentException(
                "--recommended-input-hz cannot exceed --maximum-input-hz.");
        long maximumPerBurstWindow =
            ((long)maximumInputHz * inputBurstWindowMilliseconds + 999) / 1_000;
        if (inputBurstCapacity < Math.Max(1, maximumPerBurstWindow))
            throw new ArgumentException(
                "--input-burst is too small for the hard rate and burst window.");
        var publicationPolicy = new RemoteChunkPublicationPolicy(
            publicationCadence, publicationRepresentation);
        publicationPolicy.Validate();
        return new DirectConnectOptions(
            endpoint, contentEndpoint, identity, readyFile, stopFile, summaryFile,
            accountingStartFile, accountingReadyFile, accountingStopFile,
            accountingSummaryFile, clientProfile,
            simulationFrequencyHz, maximumInputHz, recommendedInputHz,
            inputBurstCapacity, inputBurstWindowMilliseconds,
            networkServiceDelayMilliseconds, smoke,
            maximumParticipants, admissionBurstPerAddress, telemetryLevel,
            publicationPolicy);

        static string RequiredPath(string argument, string prefix)
        {
            string value = argument[prefix.Length..];
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{prefix[..^1]} cannot be empty.");
            return Path.GetFullPath(value);
        }

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
    int ActorPresentationBytes,
    string ManifestHash,
    string PlanHash,
    string CatalogFingerprint,
    string WorldPackFingerprint,
    int WorldChunks,
    string ClientProfile,
    ulong ClientProfileRevision,
    string ClientProfileHash,
    ulong ServerPerformanceProfileRevision,
    string ServerPerformanceProfileHash,
    ushort SimulationFrequencyHz,
    uint MaximumNetworkServiceIntervalMicroseconds,
    ushort MaximumInputHz,
    ushort RecommendedInputHz,
    ushort InputBurstCapacity,
    ushort InputBurstWindowMilliseconds,
    int MaximumParticipants,
    ulong ServerGeneration,
    string PublicRepresentation,
    uint PublicImmediateSteps,
    uint PublicNearSteps,
    uint PublicFarSteps);

internal readonly record struct DirectConnectSummary(
    ulong ServerGeneration,
    ulong SimulationTick,
    ulong Joins,
    ulong Leaves,
    int Participants,
    int EstablishedSessions,
    bool CleanRetirement);
