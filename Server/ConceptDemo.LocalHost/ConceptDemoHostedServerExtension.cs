using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UltraSim.Client.Local;
using UltraSim.Game.Application;
using UltraSim.Game.Chat;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Server.ConceptDemo;

public readonly record struct ConceptDemoHostedServerOptions(
    IPEndPoint GameplayEndPoint,
    IPEndPoint ContentEndPoint,
    int MaximumParticipants = 8,
    string ClientProfile = "third-person")
{
    public static ConceptDemoHostedServerOptions LocalMachineDefault => new(
        new IPEndPoint(IPAddress.Loopback, 7777),
        new IPEndPoint(IPAddress.Loopback, 7778));

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(GameplayEndPoint);
        ArgumentNullException.ThrowIfNull(ContentEndPoint);
        if (GameplayEndPoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            ContentEndPoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            GameplayEndPoint.Port < 0 || ContentEndPoint.Port <= 0 ||
            MaximumParticipants is < 2 or > 64 || string.IsNullOrWhiteSpace(ClientProfile))
            throw new ArgumentOutOfRangeException(nameof(ConceptDemoHostedServerOptions));
    }
}

/// <summary>Complete-only factory which adds Network recipients to an existing local server.</summary>
public sealed class ConceptDemoHostedServerExtensionFactory : ILocalPlayableServerExtensionFactory
{
    private readonly ConceptDemoHostedServerOptions _options;

    public ConceptDemoHostedServerExtensionFactory(ConceptDemoHostedServerOptions options = default)
    {
        _options = options == default
            ? ConceptDemoHostedServerOptions.LocalMachineDefault
            : options;
        _options.Validate();
    }

    public ILocalPlayableServerExtension Create(LocalPlayableServerContext context) =>
        new ConceptDemoHostedServerExtension(context, _options);
}

/// <summary>
/// Network/content deployment adapter for one already-running internal Concept Demo server. The
/// adapter borrows the Game generation and local participant; stopping it retires remote sessions
/// only and leaves the internal server and host player's packet-free control path alive.
/// </summary>
public sealed class ConceptDemoHostedServerExtension : ILocalPlayableServerExtension
{
    private readonly LocalPlayableServerContext _context;
    private readonly ConceptDemoHostedServerOptions _options;
    private readonly SharedPlayableAuthority _authority;
    private readonly RemoteChatDeliveryService _chatDelivery;
    private readonly RemoteChatLocalRegistration _localChat;
    private readonly SharedPlayableParticipant[] _participantScratch;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private HostedUdpAdmissionHost? _host;
    private ConceptDemoContentDeployment? _content;
    private NetworkServerIdentity? _identity;
    private ApplicationRealmId _realm;
    private string? _fingerprint;
    private string? _detail;
    private bool _disposed;

    internal ConceptDemoHostedServerExtension(
        LocalPlayableServerContext context,
        ConceptDemoHostedServerOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options;
        var delivery = new RemoteChatDeliveryService(
            context.Runtime.Game,
            context.Chat,
            new RemoteChatServerLimits(
                options.MaximumParticipants,
                checked(options.MaximumParticipants * 64),
                64,
                8,
                2_000,
                1_000));
        try
        {
            var local = new SharedPlayableParticipant(
                context.LocalParticipant,
                context.LocalGuestIdentity,
                context.LocalControlSession,
                context.LocalEntities,
                context.LocalPublicEntity,
                Slot: 0);
            _authority = SharedPlayableAuthority.AttachExisting(
                context.WorldPack,
                context.Composition,
                context.Runtime,
                context.Admission,
                context.Chat,
                delivery,
                context.Scene,
                local,
                new SharedPlayableAuthorityLimits(options.MaximumParticipants));
            _chatDelivery = delivery;
            _localChat = delivery.RegisterLocal(
                context.LocalParticipant,
                new LocalChatRecipient(context.LocalChat));
            _participantScratch = new SharedPlayableParticipant[options.MaximumParticipants];
        }
        catch
        {
            delivery.Dispose();
            throw;
        }
        _detail = "Hosting is available; the internal server is still local-only.";
    }

    public bool OwnsSharedChatHandoff => true;

    public void Pump()
    {
        EnsureOperational();
        _host?.Pump();
        _ = _chatDelivery.TryHandoffPendingSpeech();
    }

    public bool TrySetHostingEnabled(bool enabled, out string? detail)
    {
        EnsureOperational();
        if (enabled == (_host is not null))
        {
            detail = _detail;
            return true;
        }
        if (!enabled)
        {
            StopHosting();
            _detail = "Remote hosting stopped; the local internal server remains active.";
            detail = _detail;
            return true;
        }

        NetworkServerIdentity? identity = null;
        ConceptDemoContentDeployment? content = null;
        HostedUdpAdmissionHost? host = null;
        try
        {
            Directory.CreateDirectory(_context.IdentityRoot);
            identity = HostedIdentityStore.LoadOrCreateIdentity(_context.IdentityRoot);
            ApplicationRealmId realm = HostedIdentityStore.LoadOrCreateRealm(_context.IdentityRoot);
            content = ConceptDemoContentDeployment.Create(realm, _options.ContentEndPoint);
            RemoteClientProfileAdvertisement profile =
                ConceptDemoClientProfiles.Create(_options.ClientProfile);
            var applications = new SharedRemoteGuestPlayableApplicationFactory(
                _authority,
                realm,
                content.Authorization,
                [UiDefinitionIdentity.From(InventoryUi.Definition)],
                profile,
                firstParticipantId: 1);
            var shared = SharedUdpProtocolLimits.Default with
            {
                MaximumSessions = _options.MaximumParticipants - 1,
            };
            host = new HostedUdpAdmissionHost(
                _options.GameplayEndPoint,
                identity,
                NetworkProtocolLimits.Default,
                UdpProtocolLimits.Default,
                shared,
                HostedUdpAdmissionLimits.Default with
                {
                    MaximumChallengesPerAddressPerBucket = 64,
                    MaximumValidationsPerAddressPerBucket = 64,
                },
                applications,
                UltraSimGameProtocol.Manifest);
            _realm = realm;
            _fingerprint = HostedIdentityStore.Fingerprint(identity.PublicKey);
            _identity = identity;
            _content = content;
            _host = host;
            identity = null;
            content = null;
            host = null;
            _detail = $"Hosting the existing generation {_authority.GetSnapshot().GenerationId}.";
            detail = _detail;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            host?.Dispose();
            content?.Dispose();
            identity?.Dispose();
            _detail = $"Hosting could not start: {exception.Message}";
            detail = _detail;
            return false;
        }
    }

    public LocalPlayableServerExtensionSnapshot GetSnapshot()
    {
        EnsureOperational();
        SharedPlayableAuthoritySnapshot authority = _authority.GetSnapshot();
        return new LocalPlayableServerExtensionSnapshot(
            Available: true,
            HostingEnabled: _host is not null,
            Endpoint: _host?.LocalEndPoint.ToString(),
            ApplicationRealm: _realm.IsValid ? _realm.ToString() : null,
            DeploymentFingerprint: _fingerprint,
            RemoteParticipants: Math.Max(0, authority.ParticipantCount - 1),
            ServerGenerationId: authority.GenerationId,
            Detail: _detail);
    }

    public int CopyPublicPresentationMetadata(
        Span<UltraSim.Client.Core.ClientPublicPresentationMetadata> destination)
    {
        EnsureOperational();
        int count = _authority.CopyParticipants(_participantScratch);
        if (count < 0 || destination.Length < count) return -1;
        ulong generation = _authority.GetSnapshot().GenerationId;
        for (int index = 0; index < count; index++)
        {
            SharedPlayableParticipant participant = _participantScratch[index];
            destination[index] = new UltraSim.Client.Core.ClientPublicPresentationMetadata(
                generation,
                participant.PublicEntity,
                1,
                UltraSim.Client.Core.ClientPublicPresentationFlags.NameplateEligible,
                participant.GuestIdentity.DisplayName!);
        }
        return count;
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        StopHosting();
        _localChat.Dispose();
        _authority.Dispose();
        _disposed = true;
    }

    private void StopHosting()
    {
        _host?.Dispose();
        _host = null;
        _content?.Dispose();
        _content = null;
        _identity?.Dispose();
        _identity = null;
        _realm = default;
        _fingerprint = null;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "The embedded hosted adapter belongs to the internal server owner thread.");
    }

    private sealed class LocalChatRecipient : IRemoteChatLocalRecipient
    {
        private readonly ILocalPlayableChatSink _sink;
        internal LocalChatRecipient(ILocalPlayableChatSink sink) => _sink = sink;
        public bool CanPublish => _sink.CanPublish;
        public bool TryPublishSpatial(in GameSpeechFact fact) =>
            _sink.TryPublishSpatial(in fact);
    }
}

internal static class HostedIdentityStore
{
    internal static NetworkServerIdentity LoadOrCreateIdentity(string directory)
    {
        string path = Path.Combine(directory, "deployment-key.pkcs8");
        if (!File.Exists(path))
        {
            using NetworkServerIdentity created = NetworkServerIdentity.CreateEphemeral();
            byte[] key = created.ExportPkcs8PrivateKey();
            try { WriteNewSecret(path, key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        byte[] stored = File.ReadAllBytes(path);
        try
        {
            if (stored.Length > 4_096 ||
                !NetworkServerIdentity.TryCreateFromPkcs8(
                    stored, out NetworkServerIdentity? identity))
                throw new InvalidDataException(
                    "The embedded deployment identity is not one canonical P-256 key.");
            return identity!;
        }
        finally { CryptographicOperations.ZeroMemory(stored); }
    }

    internal static ApplicationRealmId LoadOrCreateRealm(string directory)
    {
        string path = Path.Combine(directory, "application-realm.txt");
        if (File.Exists(path))
        {
            string value = File.ReadAllText(path, new UTF8Encoding(false, true)).TrimEnd('\r', '\n');
            if (!ApplicationRealmId.TryParse(value, out ApplicationRealmId realm) ||
                !string.Equals(value, realm.ToString(), StringComparison.Ordinal))
                throw new InvalidDataException("The embedded ApplicationRealmId is noncanonical.");
            return realm;
        }
        Span<byte> random = stackalloc byte[16];
        RandomNumberGenerator.Fill(random);
        ulong high = BinaryPrimitives.ReadUInt64LittleEndian(random);
        ulong low = BinaryPrimitives.ReadUInt64LittleEndian(random[8..]);
        if (high == 0 && low == 0) low = 1;
        var created = new ApplicationRealmId(high, low);
        WriteNewSecret(path, Encoding.ASCII.GetBytes(created + Environment.NewLine));
        return created;
    }

    internal static string Fingerprint(ReadOnlySpan<byte> key)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(key, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteNewSecret(string path, ReadOnlySpan<byte> bytes)
    {
        string temporary = path + $".{Environment.ProcessId}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
