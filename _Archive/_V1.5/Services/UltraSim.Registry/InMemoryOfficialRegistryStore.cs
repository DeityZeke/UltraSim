using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace UltraSim.Registry;

public sealed class InMemoryOfficialRegistryStore
{
    private readonly ConcurrentDictionary<string, RegisteredServerRecord> _recordsByRegistrationId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _registrationIdByServerId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _registrationIdByLeaseId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastUnverifiedRegisterByPublisher = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HostBanRecord> _hostBansByHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _persistenceEnabled;
    private readonly string? _stateFilePath;
    private readonly RegistryRuntimeMetrics _runtimeMetrics;
    private readonly OfficialRegistryOptions _configuredOptions;
    private long _dirtyPersistenceVersion;
    private long _persistedPersistenceVersion;
    private int _persistenceFlushInProgress;
    private long _queryRevision;
    private int _allTimePeakPlayers;
    private long _allTimePeakPlayersUtcTicks;
    private static readonly JsonSerializerOptions PersistenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public InMemoryOfficialRegistryStore(
        IOptions<OfficialRegistryOptions> options,
        RegistryRuntimeMetrics runtimeMetrics)
    {
        OfficialRegistryOptions configured = options.Value;
        _configuredOptions = configured;
        _runtimeMetrics = runtimeMetrics;
        _persistenceEnabled = configured.EnableStatePersistence && !string.IsNullOrWhiteSpace(configured.StateFilePath);
        _stateFilePath = _persistenceEnabled
            ? Path.GetFullPath(configured.StateFilePath, AppContext.BaseDirectory)
            : null;

        LoadState(DateTimeOffset.UtcNow);
        _persistedPersistenceVersion = 0;
        _dirtyPersistenceVersion = 0;
    }

    public OfficialRegistryRegisterResponse Register(
        OfficialRegistryRegisterRequest request,
        string publisherFingerprint,
        OfficialRegistryOptions options,
        DateTimeOffset nowUtc)
    {
        if (request.PublishMode == DiscoveryPublishMode.Disabled)
            throw new InvalidOperationException("Disabled publish mode cannot be registered.");

        ValidateAdvertisedInfo(request.AdvertisedInfo, options);

        if (IsHostBanned(publisherFingerprint))
            throw new InvalidOperationException("Registration host is banned.");

        if (request.PublishMode == DiscoveryPublishMode.Unverified &&
            options.UnverifiedRegisterCooldownSeconds > 0 &&
            _lastUnverifiedRegisterByPublisher.TryGetValue(publisherFingerprint, out DateTimeOffset previousRegisterUtc) &&
            previousRegisterUtc.AddSeconds(options.UnverifiedRegisterCooldownSeconds) > nowUtc)
        {
            throw new InvalidOperationException("Unverified registration cooldown is active.");
        }

        string resolvedServerId = ResolveServerId(request);
        ServerListingTrustLevel trustLevel = ResolveTrustLevel(request, resolvedServerId, options);
        ServerListingVisibility visibility = request.AdvertisedInfo.Visibility;
        string registrationId = ResolveRegistrationId(request, publisherFingerprint, resolvedServerId, trustLevel);
        string leaseId = Guid.NewGuid().ToString("N");
        string heartbeatToken = Guid.NewGuid().ToString("N");
        DateTimeOffset expiresAtUtc = nowUtc.AddSeconds(Math.Max(5, options.LeaseSeconds));

        ServerDiscoveryStatusInfo statusInfo = request.StatusInfo with
        {
            LastHeartbeatUtc = nowUtc,
        };

        RegisteredServerRecord? existingRecord = null;
        if (_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? foundRecord))
            existingRecord = foundRecord;

        string effectiveServerId = existingRecord?.ServerId ?? resolvedServerId;
        ServerListingTrustLevel effectiveTrustLevel = ResolveEffectiveTrustLevel(trustLevel, existingRecord);
        ServerListingVisibility effectiveVisibility = ResolveVisibilityOnRegister(existingRecord, visibility, trustLevel);

        ServerAdvertisedInfo advertisedInfo = MergeAdvertisedInfo(
            existingRecord,
            request.AdvertisedInfo,
            effectiveServerId);
        ValidateAdvertisedInfo(advertisedInfo, options);

        if (existingRecord?.LeaseId is string previousLeaseId)
            _registrationIdByLeaseId.TryRemove(previousLeaseId, out _);

        RegisteredServerRecord record = new(
            RegistrationId: registrationId,
            ServerId: effectiveServerId,
            LeaseId: leaseId,
            HeartbeatToken: heartbeatToken,
            PublisherFingerprint: publisherFingerprint,
            TrustLevel: effectiveTrustLevel,
            Visibility: effectiveVisibility,
            HealthState: ServerListingHealthState.Pending,
            LifecycleState: ResolveLifecycleStateOnRegister(existingRecord, effectiveTrustLevel),
            AdvertisedInfo: advertisedInfo,
            StatusInfo: statusInfo,
            CreatedAtUtc: existingRecord?.CreatedAtUtc ?? nowUtc,
            LastRegisterUtc: nowUtc,
            LastPushUtc: nowUtc,
            LastSeenUtc: nowUtc,
            LastExplicitOfflineUtc: existingRecord?.LastExplicitOfflineUtc,
            LastSuccessfulPollUtc: existingRecord?.LastSuccessfulPollUtc,
            LastFailedPollUtc: existingRecord?.LastFailedPollUtc,
            DelistedUtc: ResolveDelistedUtcOnRegister(existingRecord, effectiveTrustLevel),
            OwnerAccountId: existingRecord?.OwnerAccountId,
            OwnershipClaimRequestedUtc: existingRecord?.OwnershipClaimRequestedUtc,
            OwnershipVerificationError: existingRecord?.OwnershipVerificationError,
            OwnershipTokenHash: existingRecord?.OwnershipTokenHash,
            VerifiedAtUtc: existingRecord?.VerifiedAtUtc,
            VerifiedTrustLevel: existingRecord?.VerifiedTrustLevel ?? ResolveVerifiedTrustLevel(existingRecord, effectiveTrustLevel),
            RecentSamples: AppendHistorySample(
                existingRecord?.RecentSamples,
                nowUtc,
                statusInfo,
                ServerListingHealthState.Pending,
                options),
            AggregateSamples: AppendAggregateHistorySample(
                existingRecord?.AggregateSamples,
                nowUtc,
                statusInfo,
                ServerListingHealthState.Pending,
                options),
            AllTimePeakUsers: existingRecord?.AllTimePeakUsers ?? 0,
            AllTimePeakUsersUtc: existingRecord?.AllTimePeakUsersUtc,
            ConsecutivePollFailures: 0,
            ExpiresAtUtc: expiresAtUtc,
            PollSuccessCount: existingRecord?.PollSuccessCount ?? 0,
            PollLatencyMsTotal: existingRecord?.PollLatencyMsTotal ?? 0,
            PollLatencyMsMax: existingRecord?.PollLatencyMsMax ?? 0,
            LastPollLatencyMs: existingRecord?.LastPollLatencyMs,
            PollFailureCount: existingRecord?.PollFailureCount ?? 0);

        record = ApplyOwnershipTokenHashHint(record, request.OwnershipTokenHashHint, nowUtc);

        _recordsByRegistrationId[registrationId] = record;
        _registrationIdByServerId[effectiveServerId] = registrationId;
        _registrationIdByLeaseId[leaseId] = registrationId;

        if (trustLevel == ServerListingTrustLevel.Unverified)
            RemoveSupersededUnverifiedRecords(publisherFingerprint, advertisedInfo, registrationId);

        if (trustLevel == ServerListingTrustLevel.Unverified)
            _lastUnverifiedRegisterByPublisher[publisherFingerprint] = nowUtc;

        MaybeUpdateAllTimePeakPlayers(nowUtc);
        MarkStoreChanged();
        _runtimeMetrics.RecordRegister();

        return new OfficialRegistryRegisterResponse(
            leaseId,
            effectiveServerId,
            heartbeatToken,
            expiresAtUtc,
            effectiveTrustLevel,
            effectiveVisibility,
            registrationId);
    }

    public bool TryHeartbeat(
        string leaseId,
        OfficialRegistryHeartbeatRequest request,
        OfficialRegistryOptions options,
        DateTimeOffset nowUtc,
        out OfficialRegistryRegisterResponse? response)
    {
        response = null;
        if (!_registrationIdByLeaseId.TryGetValue(leaseId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (!RegistryAuth.ConstantTimeEquals(record.HeartbeatToken ?? string.Empty, request.HeartbeatToken ?? string.Empty))
            return false;

        DateTimeOffset expiresAtUtc = nowUtc.AddSeconds(Math.Max(5, options.LeaseSeconds));
        ServerAdvertisedInfo advertisedInfo = request.AdvertisedInfo == null
            ? record.AdvertisedInfo
            : MergeAdvertisedInfo(record, request.AdvertisedInfo, record.ServerId);
        ValidateAdvertisedInfo(advertisedInfo, _configuredOptions);
        ServerDiscoveryStatusInfo statusInfo = request.StatusInfo with
        {
            LastHeartbeatUtc = nowUtc,
        };

        RegisteredServerRecord updated = record with
        {
            HealthState = ServerListingHealthState.Online,
            AdvertisedInfo = advertisedInfo,
            StatusInfo = statusInfo,
            LastPushUtc = nowUtc,
            LastSeenUtc = nowUtc,
            RecentSamples = AppendHistorySample(record.RecentSamples, nowUtc, statusInfo, ServerListingHealthState.Online, options),
            AggregateSamples = AppendAggregateHistorySample(record.AggregateSamples, nowUtc, statusInfo, ServerListingHealthState.Online, options),
            ConsecutivePollFailures = 0,
            ExpiresAtUtc = expiresAtUtc,
        };

        updated = UpdateRecordAllTimePeak(updated, nowUtc);

        updated = ApplyOwnershipTokenHashHint(updated, request.OwnershipTokenHashHint, nowUtc);

        _recordsByRegistrationId[registrationId] = updated;
        MaybeUpdateAllTimePeakPlayers(nowUtc);
        response = new OfficialRegistryRegisterResponse(
            updated.LeaseId!,
            updated.ServerId,
            updated.HeartbeatToken!,
            updated.ExpiresAtUtc,
            updated.TrustLevel,
            updated.Visibility,
            updated.RegistrationId);
        MarkStoreChanged();
        _runtimeMetrics.RecordHeartbeat();
        return true;
    }

    public bool TryApplyPresence(
        string serverId,
        OfficialRegistryPresenceRequest request,
        DateTimeOffset nowUtc,
        out ServerDiscoveryRow? row)
    {
        row = null;
        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (string.IsNullOrWhiteSpace(record.HeartbeatToken) ||
            !RegistryAuth.ConstantTimeEquals(record.HeartbeatToken, request.HeartbeatToken ?? string.Empty))
            return false;

        ServerListingHealthState healthState = request.Signal switch
        {
            OfficialRegistryPresenceSignalKind.Startup => ServerListingHealthState.Online,
            OfficialRegistryPresenceSignalKind.Online => ServerListingHealthState.Online,
            OfficialRegistryPresenceSignalKind.Offline => ServerListingHealthState.Offline,
            OfficialRegistryPresenceSignalKind.Shutdown => ServerListingHealthState.Offline,
            _ => record.HealthState,
        };

        DateTimeOffset? lastSeenUtc = healthState == ServerListingHealthState.Online
            ? nowUtc
            : record.LastSeenUtc;
        DateTimeOffset? explicitOfflineUtc = healthState == ServerListingHealthState.Offline
            ? nowUtc
            : record.LastExplicitOfflineUtc;
        ServerAdvertisedInfo advertisedInfo = request.AdvertisedInfo == null
            ? record.AdvertisedInfo
            : MergeAdvertisedInfo(record, request.AdvertisedInfo, record.ServerId);
        ValidateAdvertisedInfo(advertisedInfo, _configuredOptions);

        RegisteredServerRecord updated = record with
        {
            HealthState = healthState,
            AdvertisedInfo = advertisedInfo,
            StatusInfo = request.StatusInfo ?? record.StatusInfo,
            LastPushUtc = nowUtc,
            LastSeenUtc = lastSeenUtc,
            LastExplicitOfflineUtc = explicitOfflineUtc,
            RecentSamples = AppendHistorySample(record.RecentSamples, nowUtc, request.StatusInfo ?? record.StatusInfo, healthState, _configuredOptions),
            AggregateSamples = AppendAggregateHistorySample(record.AggregateSamples, nowUtc, request.StatusInfo ?? record.StatusInfo, healthState, _configuredOptions),
            ConsecutivePollFailures = healthState == ServerListingHealthState.Online ? 0 : record.ConsecutivePollFailures,
        };

        updated = UpdateRecordAllTimePeak(updated, nowUtc);

        updated = ApplyOwnershipTokenHashHint(updated, request.OwnershipTokenHashHint, nowUtc);

        _recordsByRegistrationId[registrationId] = updated;
        MaybeUpdateAllTimePeakPlayers(nowUtc);
        row = ToRow(updated);
        MarkStoreChanged();
        _runtimeMetrics.RecordPresence();
        return true;
    }

    public bool TryUpdateMetadata(
        string serverId,
        OfficialRegistryMetadataUpdateRequest request,
        DateTimeOffset nowUtc,
        out ServerDiscoveryRow? row)
    {
        row = null;
        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (string.IsNullOrWhiteSpace(record.HeartbeatToken) ||
            !RegistryAuth.ConstantTimeEquals(record.HeartbeatToken, request.HeartbeatToken ?? string.Empty))
            return false;

        ServerAdvertisedInfo advertisedInfo = MergeAdvertisedInfo(record, request.AdvertisedInfo, record.ServerId);
        ValidateAdvertisedInfo(advertisedInfo, _configuredOptions);

        ServerListingVisibility visibility = advertisedInfo.Visibility;
        ServerListingLifecycleState lifecycleState = record.LifecycleState;
        DateTimeOffset? delistedUtc = record.DelistedUtc;

        if (record.LifecycleState == ServerListingLifecycleState.Delisted &&
            record.TrustLevel != ServerListingTrustLevel.Unverified)
        {
            lifecycleState = ServerListingLifecycleState.Active;
            delistedUtc = null;
        }

        RegisteredServerRecord updated = record with
        {
            Visibility = visibility,
            LifecycleState = lifecycleState,
            AdvertisedInfo = advertisedInfo,
            LastPushUtc = nowUtc,
            DelistedUtc = delistedUtc,
        };

        _recordsByRegistrationId[registrationId] = updated;
        row = ToRow(updated);
        MarkStoreChanged();
        _runtimeMetrics.RecordMetadataUpdate();
        return true;
    }

    public bool TrySetLifecycleState(
        string serverId,
        ServerListingLifecycleState lifecycleState,
        DateTimeOffset nowUtc,
        out ServerDiscoveryDetail? detail)
    {
        detail = null;
        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (lifecycleState != ServerListingLifecycleState.Active &&
            lifecycleState != ServerListingLifecycleState.Disabled)
        {
            throw new InvalidOperationException("Only Active and Disabled lifecycle transitions are allowed through the admin API.");
        }

        RegisteredServerRecord updated = record with
        {
            LifecycleState = lifecycleState,
            DelistedUtc = lifecycleState == ServerListingLifecycleState.Active ? null : record.DelistedUtc,
            LastPushUtc = nowUtc,
        };

        _recordsByRegistrationId[registrationId] = updated;
        detail = ToDetail(updated, nowUtc);
        MarkStoreChanged();
        return true;
    }

    public bool TryResetRetention(
        string serverId,
        DateTimeOffset nowUtc,
        out ServerDiscoveryDetail? detail)
    {
        detail = null;
        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (record.LifecycleState == ServerListingLifecycleState.Disabled)
            throw new InvalidOperationException("Retention reset is not available for disabled listings.");

        RegisteredServerRecord updated = record.LifecycleState switch
        {
            ServerListingLifecycleState.Active => record with
            {
                LastPushUtc = nowUtc,
            },
            ServerListingLifecycleState.Delisted => record with
            {
                DelistedUtc = nowUtc,
            },
            _ => record,
        };

        _recordsByRegistrationId[registrationId] = updated;
        detail = ToDetail(updated, DateTimeOffset.UtcNow);
        MarkStoreChanged();
        return true;
    }

    public bool TrySetVoteCount(string serverId, int voteCount, out ServerDiscoveryDetail? detail)
    {
        detail = null;

        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        int normalizedVoteCount = Math.Max(0, voteCount);
        if (record.VoteCount == normalizedVoteCount)
        {
            detail = ToDetail(record, DateTimeOffset.UtcNow);
            return true;
        }

        RegisteredServerRecord updated = record with
        {
            VoteCount = normalizedVoteCount
        };

        _recordsByRegistrationId[registrationId] = updated;
        detail = ToDetail(updated, DateTimeOffset.UtcNow);
        MarkStoreChanged();
        return true;
    }

    public OfficialRegistryOwnerClaimResponse ClaimServer(
        OfficialRegistryOwnerClaimRequest request,
        DateTimeOffset nowUtc)
    {
        ValidateOwnerClaimRequest(request);

        RegisteredServerRecord? record = FindClaimCandidate(request);
        if (record == null)
        {
            return new OfficialRegistryOwnerClaimResponse(
                OfficialRegistryOwnerClaimState.NoServerDetected,
                null,
                null,
                "No temporary listing matched the requested server target.");
        }

        if (!string.IsNullOrWhiteSpace(record.OwnerAccountId) &&
            !StringComparer.Ordinal.Equals(record.OwnerAccountId, request.OwnerAccountId))
        {
            return new OfficialRegistryOwnerClaimResponse(
                OfficialRegistryOwnerClaimState.OwnershipConflict,
                record.ServerId,
                record.RegistrationId,
                "That listing is already associated with a different account.",
                record.TrustLevel,
                record.AdvertisedInfo.ConnectHint);
        }

        string ownershipTokenHash = ComputeOwnershipTokenHash(request.OwnershipToken);
        string pendingMessage = "Claim accepted. Verification will complete on the next successful poll.";

        RegisteredServerRecord updated = record with
        {
            OwnerAccountId = request.OwnerAccountId.Trim(),
            OwnershipClaimRequestedUtc = nowUtc,
            OwnershipVerificationError = null,
            OwnershipTokenHash = ownershipTokenHash,
            VerifiedAtUtc = null,
            VerifiedTrustLevel = ResolveVerifiedTrustLevel(record, record.TrustLevel),
            AdvertisedInfo = record.AdvertisedInfo with
            {
                DisplayName = request.DisplayName.Trim(),
                ConnectHint = BuildRequestedConnectHint(request),
                Description = NormalizeOptional(request.Description),
                WebsiteUrl = NormalizeOptional(request.WebsiteUrl),
                DiscordUrl = NormalizeOptional(request.DiscordUrl),
            },
        };

        _recordsByRegistrationId[record.RegistrationId] = updated;
        MarkStoreChanged();

        return new OfficialRegistryOwnerClaimResponse(
            OfficialRegistryOwnerClaimState.PendingVerification,
            updated.ServerId,
            updated.RegistrationId,
            pendingMessage,
            updated.TrustLevel,
            updated.AdvertisedInfo.ConnectHint);
    }

    public bool TryUpdateOwnerMetadata(
        string serverId,
        OfficialRegistryOwnerMetadataUpdateRequest request,
        DateTimeOffset nowUtc,
        out ServerDiscoveryDetail? detail)
    {
        detail = null;
        ValidateOwnerMetadataRequest(request);

        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (!StringComparer.Ordinal.Equals(record.OwnerAccountId, request.OwnerAccountId.Trim()))
            return false;

        RegisteredServerRecord updated = record with
        {
            AdvertisedInfo = record.AdvertisedInfo with
            {
                DisplayName = request.DisplayName.Trim(),
                ConnectHint = $"{request.TargetHost.Trim()}:{request.TargetPort}",
                Description = NormalizeOptional(request.Description),
                WebsiteUrl = NormalizeOptional(request.WebsiteUrl),
                DiscordUrl = NormalizeOptional(request.DiscordUrl),
            },
            LastPushUtc = nowUtc,
        };

        _recordsByRegistrationId[registrationId] = updated;
        MarkStoreChanged();
        detail = ToDetail(updated, DateTimeOffset.UtcNow);
        return true;
    }

    public bool TryUpdateOwnerToken(
        string serverId,
        OfficialRegistryOwnerTokenUpdateRequest request,
        DateTimeOffset nowUtc,
        out ServerDiscoveryDetail? detail)
    {
        detail = null;
        ValidateOwnerTokenUpdateRequest(request);

        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (!StringComparer.Ordinal.Equals(record.OwnerAccountId, request.OwnerAccountId.Trim()))
            return false;

        string updatedOwnershipTokenHash = ComputeOwnershipTokenHash(request.OwnershipToken);
        RegisteredServerRecord updated = record with
        {
            OwnershipTokenHash = updatedOwnershipTokenHash,
            OwnershipClaimRequestedUtc = nowUtc,
            OwnershipVerificationError = null,
            VerifiedAtUtc = null,
            TrustLevel = DemoteForVerificationLoss(record),
            LastPushUtc = nowUtc,
        };

        _recordsByRegistrationId[registrationId] = updated;
        MarkStoreChanged();
        detail = ToDetail(updated, DateTimeOffset.UtcNow);
        return true;
    }

    public bool TryDeleteOwnerClaimedServer(string serverId, string ownerAccountId)
    {
        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (!StringComparer.Ordinal.Equals(record.OwnerAccountId, ownerAccountId.Trim()))
            return false;

        _recordsByRegistrationId.TryRemove(registrationId, out _);
        _registrationIdByServerId.TryRemove(record.ServerId, out _);
        if (!string.IsNullOrWhiteSpace(record.LeaseId))
            _registrationIdByLeaseId.TryRemove(record.LeaseId, out _);

        MarkStoreChanged();
        _runtimeMetrics.RecordDelete();
        return true;
    }

    public IReadOnlyList<OfficialRegistryAdminServerRecord> GetAdminServerRecords(DateTimeOffset nowUtc)
    {
        PruneExpired(nowUtc);

        return _recordsByRegistrationId.Values
            .Select(ToAdminRecord)
            .OrderByDescending(static record => record.CreatedAtUtc)
            .ThenBy(static record => record.Row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryGetAdminServerRecord(string serverId, DateTimeOffset nowUtc, out OfficialRegistryAdminServerRecord? record)
    {
        record = null;
        PruneExpired(nowUtc);

        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? found))
            return false;

        record = ToAdminRecord(found);
        return true;
    }

    public bool TryDeleteAdminServer(string serverId)
    {
        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryRemove(registrationId, out RegisteredServerRecord? record))
            return false;

        _registrationIdByServerId.TryRemove(serverId, out _);

        if (!string.IsNullOrWhiteSpace(record.LeaseId))
            _registrationIdByLeaseId.TryRemove(record.LeaseId, out _);

        MarkStoreChanged();
        _runtimeMetrics.RecordDelete();
        return true;
    }

    public IReadOnlyList<OfficialRegistryAdminHostBanRecord> GetHostBans()
    {
        return _hostBansByHost.Values
            .OrderByDescending(static record => record.IssuedAtUtc)
            .Select(static record => new OfficialRegistryAdminHostBanRecord(
                record.Host,
                record.Reason,
                record.IssuedAtUtc,
                record.LastAppliedAtUtc))
            .ToArray();
    }

    public bool TryBanHostForServer(
        string serverId,
        string reason,
        DateTimeOffset nowUtc,
        out OfficialRegistryAdminHostBanRecord? banRecord)
    {
        banRecord = null;

        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        string? host = ResolveModerationHost(record);
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Could not resolve a host/IP to ban for that listing.");

        HostBanRecord storedBan = new(
            Host: host,
            Reason: NormalizeOptional(reason),
            IssuedAtUtc: nowUtc,
            LastAppliedAtUtc: nowUtc);
        _hostBansByHost[host] = storedBan;

        bool changed = false;
        foreach ((string registrationKey, RegisteredServerRecord currentRecord) in _recordsByRegistrationId)
        {
            if (!RecordMatchesHostBan(currentRecord, host))
                continue;

            RegisteredServerRecord updatedRecord = currentRecord with
            {
                LifecycleState = ServerListingLifecycleState.Disabled,
                LastPushUtc = nowUtc,
            };

            _recordsByRegistrationId[registrationKey] = updatedRecord;
            changed = true;
        }

        banRecord = new OfficialRegistryAdminHostBanRecord(
            storedBan.Host,
            storedBan.Reason,
            storedBan.IssuedAtUtc,
            storedBan.LastAppliedAtUtc);

        if (changed || _hostBansByHost.ContainsKey(host))
            MarkStoreChanged();

        return true;
    }

    public bool TryLiftHostBan(string host)
    {
        string normalizedHost = NormalizeHostOrValue(host);
        if (string.IsNullOrWhiteSpace(normalizedHost))
            return false;

        bool removed = _hostBansByHost.TryRemove(normalizedHost, out _);
        if (removed)
            MarkStoreChanged();

        return removed;
    }

    public bool TryDelete(string leaseId, string heartbeatToken)
    {
        if (!_registrationIdByLeaseId.TryGetValue(leaseId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (!RegistryAuth.ConstantTimeEquals(record.HeartbeatToken ?? string.Empty, heartbeatToken))
            return false;

        _recordsByRegistrationId.TryRemove(registrationId, out _);
        _registrationIdByLeaseId.TryRemove(leaseId, out _);
        _registrationIdByServerId.TryRemove(record.ServerId, out _);
        MarkStoreChanged();
        _runtimeMetrics.RecordDelete();
        return true;
    }

    private RegisteredServerRecord? FindClaimCandidate(OfficialRegistryOwnerClaimRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedServerId) &&
            _registrationIdByServerId.TryGetValue(request.RequestedServerId.Trim(), out string? requestedRegistrationId) &&
            _recordsByRegistrationId.TryGetValue(requestedRegistrationId, out RegisteredServerRecord? requestedRecord))
        {
            return requestedRecord;
        }

        string connectHint = BuildRequestedConnectHint(request);
        foreach (RegisteredServerRecord record in _recordsByRegistrationId.Values)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(record.AdvertisedInfo.ConnectHint, connectHint))
                return record;
        }

        return null;
    }

    public IReadOnlyList<ServerDiscoveryRow> GetVisibleRows(DateTimeOffset nowUtc)
    {
        PruneExpired(nowUtc);

        List<ServerDiscoveryRow> rows = new();
        foreach (RegisteredServerRecord record in _recordsByRegistrationId.Values)
        {
            if (record.Visibility != ServerListingVisibility.Public)
                continue;

            if (record.LifecycleState != ServerListingLifecycleState.Active)
                continue;

            if (!ShouldAppearInVisibleResults(record))
                continue;

            rows.Add(ToRow(record));
        }

        rows.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return rows;
    }

    public IReadOnlyList<ServerDiscoveryRow> QueryVisibleRows(
        OfficialRegistryServerQuery query,
        DateTimeOffset nowUtc)
    {
        IReadOnlyList<ServerDiscoveryRow> visibleRows = GetVisibleRows(nowUtc);
        IEnumerable<ServerDiscoveryRow> rows = visibleRows;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim();
            rows = rows.Where(row =>
                (!string.IsNullOrWhiteSpace(row.DisplayName) && row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(row.ServerId) && row.ServerId.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(row.ConnectHint) && row.ConnectHint.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            rows = rows.Where(row => StringComparer.OrdinalIgnoreCase.Equals(row.Region, query.Region));
        }

        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            rows = rows.Where(row => StringComparer.OrdinalIgnoreCase.Equals(row.PrimaryLanguage, query.Language));
        }

        if (query.TrustLevel.HasValue)
            rows = rows.Where(row => row.TrustLevel == query.TrustLevel.Value);

        if (query.PasswordRequired.HasValue)
            rows = rows.Where(row => row.PasswordRequired == query.PasswordRequired.Value);

        if (query.WhitelistOnly.HasValue)
            rows = rows.Where(row => row.WhitelistOnly == query.WhitelistOnly.Value);

        if (!string.IsNullOrWhiteSpace(query.CompatibilityId))
        {
            rows = rows.Where(row => StringComparer.OrdinalIgnoreCase.Equals(row.CompatibilityId, query.CompatibilityId));
        }

        rows = ApplySort(rows, query.SortBy, query.Descending);

        int offset = Math.Max(0, query.Offset);
        int limit = query.Limit <= 0 ? 100 : Math.Min(query.Limit, 200);

        return rows
            .Skip(offset)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<RegistryPollCandidate> GetPollCandidates(DateTimeOffset nowUtc)
    {
        PruneExpired(nowUtc);

        List<RegistryPollCandidate> candidates = new();
        foreach (RegisteredServerRecord record in _recordsByRegistrationId.Values)
        {
            if (record.LifecycleState != ServerListingLifecycleState.Active)
                continue;

            if (record.Visibility != ServerListingVisibility.Public)
                continue;

            if (string.IsNullOrWhiteSpace(record.AdvertisedInfo.StatusEndpointUrl))
                continue;

            string? pollHost = TryExtractHostFromStatusEndpointUrl(record.AdvertisedInfo.StatusEndpointUrl);
            if (!string.IsNullOrWhiteSpace(pollHost) && IsHostBanned(pollHost))
                continue;

            candidates.Add(new RegistryPollCandidate(
                record.RegistrationId,
                record.ServerId,
                record.AdvertisedInfo.StatusEndpointUrl!));
        }

        return candidates;
    }

    public bool TryGetByServerId(string serverId, DateTimeOffset nowUtc, out ServerDiscoveryDetail? detail)
    {
        PruneExpired(nowUtc);
        detail = null;

        if (!_registrationIdByServerId.TryGetValue(serverId, out string? registrationId))
            return false;

        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        detail = ToDetail(record, nowUtc);
        return true;
    }

    public bool ApplyPollSuccess(
        string registrationId,
        ServerDiscoveryPollStatusResponse response,
        DateTimeOffset nowUtc,
        int latencyMs)
    {
        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        if (!string.IsNullOrWhiteSpace(response.ServerId) &&
            !StringComparer.OrdinalIgnoreCase.Equals(response.ServerId, record.ServerId))
        {
            return false;
        }

        ServerDiscoveryStatusInfo statusInfo = response.StatusInfo with
        {
            LastHeartbeatUtc = nowUtc,
        };

        RegisteredServerRecord updated = record with
        {
            HealthState = ServerListingHealthState.Online,
            StatusInfo = statusInfo,
            LastSeenUtc = nowUtc,
            LastSuccessfulPollUtc = nowUtc,
            RecentSamples = AppendHistorySample(record.RecentSamples, nowUtc, statusInfo, ServerListingHealthState.Online, _configuredOptions),
            AggregateSamples = AppendAggregateHistorySample(record.AggregateSamples, nowUtc, statusInfo, ServerListingHealthState.Online, _configuredOptions),
            PollSuccessCount = record.PollSuccessCount + 1,
            PollLatencyMsTotal = record.PollLatencyMsTotal + Math.Max(0, latencyMs),
            PollLatencyMsMax = Math.Max(record.PollLatencyMsMax, Math.Max(0, latencyMs)),
            LastPollLatencyMs = Math.Max(0, latencyMs),
            ConsecutivePollFailures = 0,
        };

        updated = UpdateRecordAllTimePeak(updated, nowUtc);

        if (!string.IsNullOrWhiteSpace(record.OwnershipTokenHash))
        {
            if (StringComparer.Ordinal.Equals(record.OwnershipTokenHash, response.OwnershipTokenHash))
            {
                updated = updated with
                {
                    TrustLevel = record.VerifiedTrustLevel,
                    OwnershipVerificationError = null,
                    VerifiedAtUtc = record.VerifiedAtUtc ?? nowUtc,
                };
            }
            else
            {
                updated = updated with
                {
                    TrustLevel = DemoteForVerificationLoss(record),
                    VerifiedAtUtc = null,
                    OwnershipVerificationError = string.IsNullOrWhiteSpace(response.OwnershipTokenHash)
                        ? "Server did not provide ownership proof during polling."
                        : "Ownership token mismatch.",
                };
            }
        }

        _recordsByRegistrationId[registrationId] = updated;
        MaybeUpdateAllTimePeakPlayers(nowUtc);
        MarkStoreChanged();
        return true;
    }

    public bool ApplyPollFailure(
        string registrationId,
        DateTimeOffset nowUtc,
        int failureThreshold)
    {
        if (!_recordsByRegistrationId.TryGetValue(registrationId, out RegisteredServerRecord? record))
            return false;

        int consecutiveFailures = record.ConsecutivePollFailures + 1;
        ServerListingHealthState healthState = consecutiveFailures >= Math.Max(1, failureThreshold)
            ? ServerListingHealthState.Offline
            : ServerListingHealthState.Stale;

        RegisteredServerRecord updated = record with
        {
            HealthState = healthState,
            LastFailedPollUtc = nowUtc,
            RecentSamples = AppendHistorySample(record.RecentSamples, nowUtc, record.StatusInfo, healthState, _configuredOptions),
            AggregateSamples = AppendAggregateHistorySample(record.AggregateSamples, nowUtc, record.StatusInfo, healthState, _configuredOptions),
            PollFailureCount = record.PollFailureCount + 1,
            ConsecutivePollFailures = consecutiveFailures,
        };

        updated = UpdateRecordAllTimePeak(updated, nowUtc);

        _recordsByRegistrationId[registrationId] = updated;
        MaybeUpdateAllTimePeakPlayers(nowUtc);
        MarkStoreChanged();
        return true;
    }

    public void RunLifecycleSweep(OfficialRegistryOptions options, DateTimeOffset nowUtc)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool changed = false;
        int delistedCount = 0;
        List<string>? registrationIdsToPurge = null;

        foreach (KeyValuePair<string, RegisteredServerRecord> pair in _recordsByRegistrationId)
        {
            RegisteredServerRecord record = pair.Value;

            // Skip Disabled records — lifecycle transitions require explicit admin action.
            if (record.LifecycleState == ServerListingLifecycleState.Disabled)
                continue;

            if (record.LifecycleState == ServerListingLifecycleState.Active)
            {
                TimeSpan delistThreshold = record.TrustLevel == ServerListingTrustLevel.Unverified
                    ? TimeSpan.FromHours(Math.Max(1, options.UnverifiedDelistAfterHours))
                    : TimeSpan.FromDays(Math.Max(1, options.VerifiedDelistAfterDays));

                DateTimeOffset lastActivity = ComputeLastActivity(record);
                if (nowUtc - lastActivity >= delistThreshold)
                {
                    _recordsByRegistrationId[pair.Key] = record with
                    {
                        LifecycleState = ServerListingLifecycleState.Delisted,
                        DelistedUtc = nowUtc,
                    };
                    changed = true;
                    delistedCount++;
                }
            }
            else if (record.LifecycleState == ServerListingLifecycleState.Delisted &&
                     record.DelistedUtc.HasValue)
            {
                TimeSpan purgeThreshold = record.TrustLevel == ServerListingTrustLevel.Unverified
                    ? TimeSpan.FromHours(Math.Max(1, options.UnverifiedPurgeAfterDelistedHours))
                    : TimeSpan.FromDays(Math.Max(1, options.VerifiedPurgeAfterDelistedDays));

                if (nowUtc - record.DelistedUtc.Value >= purgeThreshold)
                {
                    registrationIdsToPurge ??= new List<string>();
                    registrationIdsToPurge.Add(pair.Key);
                }
            }
        }

        if (registrationIdsToPurge != null)
        {
            for (int i = 0; i < registrationIdsToPurge.Count; i++)
            {
                string registrationId = registrationIdsToPurge[i];
                if (!_recordsByRegistrationId.TryRemove(registrationId, out RegisteredServerRecord? removed))
                    continue;

                if (!string.IsNullOrWhiteSpace(removed.LeaseId))
                    _registrationIdByLeaseId.TryRemove(removed.LeaseId, out _);

                if (_registrationIdByServerId.TryGetValue(removed.ServerId, out string? mappedId) &&
                    StringComparer.Ordinal.Equals(mappedId, registrationId))
                {
                    _registrationIdByServerId.TryRemove(removed.ServerId, out _);
                }

                changed = true;
            }
        }

        if (changed)
            MarkStoreChanged();

        // Evict publisher cooldown entries that are past the cooldown window — they
        // serve no purpose and would otherwise accumulate indefinitely.
        if (options.UnverifiedRegisterCooldownSeconds > 0)
        {
            DateTimeOffset cooldownCutoff = nowUtc.AddSeconds(-Math.Max(1, options.UnverifiedRegisterCooldownSeconds));
            foreach (KeyValuePair<string, DateTimeOffset> pair in _lastUnverifiedRegisterByPublisher)
            {
                if (pair.Value < cooldownCutoff)
                    _lastUnverifiedRegisterByPublisher.TryRemove(pair.Key, out _);
            }
        }

        stopwatch.Stop();
        _runtimeMetrics.RecordLifecycleSweep(
            delistedCount,
            registrationIdsToPurge?.Count ?? 0,
            (long)stopwatch.Elapsed.TotalMilliseconds,
            nowUtc);
    }

    private static DateTimeOffset ComputeLastActivity(RegisteredServerRecord record)
    {
        DateTimeOffset last = record.LastRegisterUtc > record.LastPushUtc
            ? record.LastRegisterUtc
            : record.LastPushUtc;

        if (record.LastSuccessfulPollUtc.HasValue && record.LastSuccessfulPollUtc.Value > last)
            last = record.LastSuccessfulPollUtc.Value;

        return last;
    }

    private void PruneExpired(DateTimeOffset nowUtc)
    {
        bool changed = false;
        int prunedCount = 0;
        foreach (KeyValuePair<string, RegisteredServerRecord> pair in _recordsByRegistrationId)
        {
            RegisteredServerRecord record = pair.Value;
            if (string.IsNullOrWhiteSpace(record.LeaseId) || record.ExpiresAtUtc > nowUtc)
                continue;

            if (record.LeaseId is string activeLeaseId)
                changed |= _registrationIdByLeaseId.TryRemove(activeLeaseId, out _);

            _recordsByRegistrationId[pair.Key] = record with
            {
                LeaseId = null,
                HeartbeatToken = null,
                HealthState = ServerListingHealthState.Offline,
            };
            changed = true;
            prunedCount++;
        }

        if (changed)
        {
            MarkStoreChanged();
            _runtimeMetrics.RecordLeasePrune(prunedCount);
        }
    }

    private string ResolveRegistrationId(
        OfficialRegistryRegisterRequest request,
        string publisherFingerprint,
        string serverId,
        ServerListingTrustLevel trustLevel)
    {
        if (trustLevel != ServerListingTrustLevel.Unverified &&
            _registrationIdByServerId.TryGetValue(serverId, out string? existingRegistrationId))
        {
            return existingRegistrationId;
        }

        if (trustLevel == ServerListingTrustLevel.Unverified)
        {
            string? existingClaimedRegistrationId = FindExistingClaimedRegistrationId(
                serverId,
                request.AdvertisedInfo.ConnectHint,
                request.AdvertisedInfo.StatusEndpointUrl);
            if (!string.IsNullOrWhiteSpace(existingClaimedRegistrationId))
                return existingClaimedRegistrationId;

            string? existingUnverifiedRegistrationId = FindExistingUnverifiedRegistrationId(
                publisherFingerprint,
                serverId,
                request.AdvertisedInfo.ConnectHint,
                request.AdvertisedInfo.StatusEndpointUrl);
            if (!string.IsNullOrWhiteSpace(existingUnverifiedRegistrationId))
                return existingUnverifiedRegistrationId;
        }

        return Guid.NewGuid().ToString("N");
    }

    private string? FindExistingClaimedRegistrationId(
        string serverId,
        string? connectHint,
        string? statusEndpointUrl)
    {
        foreach (KeyValuePair<string, RegisteredServerRecord> pair in _recordsByRegistrationId)
        {
            RegisteredServerRecord record = pair.Value;
            if (string.IsNullOrWhiteSpace(record.OwnerAccountId) &&
                record.TrustLevel == ServerListingTrustLevel.Unverified)
            {
                continue;
            }

            if (MatchesUnverifiedIdentity(record, serverId, connectHint, statusEndpointUrl))
                return pair.Key;
        }

        return null;
    }

    private string? FindExistingUnverifiedRegistrationId(
        string publisherFingerprint,
        string serverId,
        string? connectHint,
        string? statusEndpointUrl)
    {
        foreach (KeyValuePair<string, RegisteredServerRecord> pair in _recordsByRegistrationId)
        {
            RegisteredServerRecord record = pair.Value;
            if (record.TrustLevel != ServerListingTrustLevel.Unverified)
                continue;

            if (!StringComparer.Ordinal.Equals(record.PublisherFingerprint, publisherFingerprint))
                continue;

            if (MatchesUnverifiedIdentity(record, serverId, connectHint, statusEndpointUrl))
                return pair.Key;
        }

        return null;
    }

    private void RemoveSupersededUnverifiedRecords(
        string publisherFingerprint,
        ServerAdvertisedInfo advertisedInfo,
        string keepRegistrationId)
    {
        List<string> registrationIdsToRemove = new();
        foreach (KeyValuePair<string, RegisteredServerRecord> pair in _recordsByRegistrationId)
        {
            if (StringComparer.Ordinal.Equals(pair.Key, keepRegistrationId))
                continue;

            RegisteredServerRecord record = pair.Value;
            if (record.TrustLevel != ServerListingTrustLevel.Unverified)
                continue;

            if (!StringComparer.Ordinal.Equals(record.PublisherFingerprint, publisherFingerprint))
                continue;

            if (!MatchesUnverifiedIdentity(record, advertisedInfo.ServerId, advertisedInfo.ConnectHint, advertisedInfo.StatusEndpointUrl))
                continue;

            registrationIdsToRemove.Add(pair.Key);
        }

        for (int i = 0; i < registrationIdsToRemove.Count; i++)
        {
            string registrationId = registrationIdsToRemove[i];
            if (!_recordsByRegistrationId.TryRemove(registrationId, out RegisteredServerRecord? removed))
                continue;

            if (!string.IsNullOrWhiteSpace(removed.LeaseId))
                _registrationIdByLeaseId.TryRemove(removed.LeaseId, out _);

            if (_registrationIdByServerId.TryGetValue(removed.ServerId, out string? mappedRegistrationId) &&
                StringComparer.Ordinal.Equals(mappedRegistrationId, registrationId))
            {
                _registrationIdByServerId[removed.ServerId] = keepRegistrationId;
            }
        }
    }

    private static bool MatchesUnverifiedIdentity(
        RegisteredServerRecord record,
        string serverId,
        string? connectHint,
        string? statusEndpointUrl)
    {
        if (!string.IsNullOrWhiteSpace(statusEndpointUrl) &&
            !string.IsNullOrWhiteSpace(record.AdvertisedInfo.StatusEndpointUrl))
        {
            return StringComparer.OrdinalIgnoreCase.Equals(
                record.AdvertisedInfo.StatusEndpointUrl,
                statusEndpointUrl);
        }

        if (!string.IsNullOrWhiteSpace(connectHint) &&
            !string.IsNullOrWhiteSpace(record.AdvertisedInfo.ConnectHint))
        {
            return StringComparer.OrdinalIgnoreCase.Equals(
                record.AdvertisedInfo.ConnectHint,
                connectHint);
        }

        return StringComparer.OrdinalIgnoreCase.Equals(record.ServerId, serverId);
    }

    private static ServerListingTrustLevel ResolveEffectiveTrustLevel(
        ServerListingTrustLevel requestedTrustLevel,
        RegisteredServerRecord? existingRecord)
    {
        if (requestedTrustLevel != ServerListingTrustLevel.Unverified)
            return requestedTrustLevel;

        if (existingRecord == null)
            return requestedTrustLevel;

        return string.IsNullOrWhiteSpace(existingRecord.OwnerAccountId) &&
               existingRecord.TrustLevel == ServerListingTrustLevel.Unverified
            ? requestedTrustLevel
            : existingRecord.TrustLevel;
    }

    private static ServerListingVisibility ResolveVisibilityOnRegister(
        RegisteredServerRecord? existingRecord,
        ServerListingVisibility requestedVisibility,
        ServerListingTrustLevel requestedTrustLevel)
    {
        if (requestedTrustLevel != ServerListingTrustLevel.Unverified ||
            existingRecord == null ||
            string.IsNullOrWhiteSpace(existingRecord.OwnerAccountId))
        {
            return requestedVisibility;
        }

        return existingRecord.Visibility;
    }

    private static ServerAdvertisedInfo MergeAdvertisedInfo(
        RegisteredServerRecord? existingRecord,
        ServerAdvertisedInfo incomingAdvertisedInfo,
        string serverId)
    {
        ServerAdvertisedInfo normalized = incomingAdvertisedInfo with
        {
            ServerId = serverId,
        };

        if (existingRecord == null || string.IsNullOrWhiteSpace(existingRecord.OwnerAccountId))
            return normalized;

        return normalized with
        {
            DisplayName = existingRecord.AdvertisedInfo.DisplayName,
            ConnectHint = existingRecord.AdvertisedInfo.ConnectHint,
            Description = existingRecord.AdvertisedInfo.Description,
            WebsiteUrl = existingRecord.AdvertisedInfo.WebsiteUrl,
            DiscordUrl = existingRecord.AdvertisedInfo.DiscordUrl,
        };
    }

    private static void ValidateAdvertisedInfo(ServerAdvertisedInfo advertisedInfo, OfficialRegistryOptions options)
    {
        if (string.IsNullOrWhiteSpace(advertisedInfo.StatusEndpointUrl))
            return;

        if (!Uri.TryCreate(advertisedInfo.StatusEndpointUrl, UriKind.Absolute, out Uri? statusEndpointUri))
            throw new InvalidOperationException("StatusEndpointUrl must be an absolute URI.");

        if (!statusEndpointUri.IsAbsoluteUri ||
            (!StringComparer.OrdinalIgnoreCase.Equals(statusEndpointUri.Scheme, Uri.UriSchemeHttp) &&
             !StringComparer.OrdinalIgnoreCase.Equals(statusEndpointUri.Scheme, Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException("StatusEndpointUrl must use http or https.");
        }

        string host = statusEndpointUri.Host;
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("StatusEndpointUrl must include a host.");

        if (!string.IsNullOrWhiteSpace(statusEndpointUri.Query) ||
            !string.IsNullOrWhiteSpace(statusEndpointUri.Fragment))
        {
            throw new InvalidOperationException("StatusEndpointUrl must not include a query string or fragment.");
        }

        if (SsrfGuard.IsBlockedHost(host, options.AllowPrivateStatusEndpointTargets))
            throw new InvalidOperationException("StatusEndpointUrl host is not allowed.");
    }

    private static DateTimeOffset? ResolveDelistedUtcOnRegister(
        RegisteredServerRecord? existingRecord,
        ServerListingTrustLevel trustLevel)
    {
        // Clear DelistedUtc when a verified/official re-registration reactivates the record.
        if (existingRecord?.LifecycleState == ServerListingLifecycleState.Delisted &&
            trustLevel != ServerListingTrustLevel.Unverified)
        {
            return null;
        }

        return existingRecord?.DelistedUtc;
    }

    private static ServerListingLifecycleState ResolveLifecycleStateOnRegister(
        RegisteredServerRecord? existingRecord,
        ServerListingTrustLevel trustLevel)
    {
        if (existingRecord == null)
            return ServerListingLifecycleState.Active;

        // Verified/Official re-registration reactivates a Delisted record.
        if (existingRecord.LifecycleState == ServerListingLifecycleState.Delisted &&
            trustLevel != ServerListingTrustLevel.Unverified)
        {
            return ServerListingLifecycleState.Active;
        }

        // Disabled records stay Disabled regardless — admin action required.
        // Active and Delisted-unverified records keep their current state.
        return existingRecord.LifecycleState;
    }

    private static string ResolveServerId(OfficialRegistryRegisterRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AdvertisedInfo.ServerId))
            return request.AdvertisedInfo.ServerId.Trim();

        return $"unverified-{Guid.NewGuid():N}";
    }

    private static void ValidateOwnerClaimRequest(OfficialRegistryOwnerClaimRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerAccountId))
            throw new InvalidOperationException("OwnerAccountId is required.");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("DisplayName is required.");

        if (string.IsNullOrWhiteSpace(request.TargetHost))
            throw new InvalidOperationException("TargetHost is required.");

        if (request.TargetPort < 1 || request.TargetPort > 65535)
            throw new InvalidOperationException("TargetPort must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(request.OwnershipToken))
            throw new InvalidOperationException("OwnershipToken is required.");
    }

    private static void ValidateOwnerMetadataRequest(OfficialRegistryOwnerMetadataUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerAccountId))
            throw new InvalidOperationException("OwnerAccountId is required.");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("DisplayName is required.");

        if (string.IsNullOrWhiteSpace(request.TargetHost))
            throw new InvalidOperationException("TargetHost is required.");

        if (request.TargetPort < 1 || request.TargetPort > 65535)
            throw new InvalidOperationException("TargetPort must be between 1 and 65535.");
    }

    private static void ValidateOwnerTokenUpdateRequest(OfficialRegistryOwnerTokenUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerAccountId))
            throw new InvalidOperationException("OwnerAccountId is required.");

        if (string.IsNullOrWhiteSpace(request.OwnershipToken))
            throw new InvalidOperationException("OwnershipToken is required.");
    }

    private static string BuildRequestedConnectHint(OfficialRegistryOwnerClaimRequest request)
    {
        return $"{request.TargetHost.Trim()}:{request.TargetPort}";
    }

    private static string ComputeOwnershipTokenHash(string ownershipToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ownershipToken.Trim());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static RegisteredServerRecord ApplyOwnershipTokenHashHint(
        RegisteredServerRecord record,
        string? ownershipTokenHashHint,
        DateTimeOffset nowUtc)
    {
        string? normalizedHint = NormalizeOptional(ownershipTokenHashHint);
        if (string.IsNullOrWhiteSpace(record.OwnershipTokenHash) || string.IsNullOrWhiteSpace(normalizedHint))
            return record;

        if (StringComparer.Ordinal.Equals(record.OwnershipTokenHash, normalizedHint))
            return record;

        return record with
        {
            TrustLevel = DemoteForVerificationLoss(record),
            OwnershipClaimRequestedUtc = nowUtc,
            OwnershipVerificationError = "Server ownership token changed. Update the portal token and wait for the next poll.",
            VerifiedAtUtc = null,
        };
    }

    private static ServerListingTrustLevel ResolveVerifiedTrustLevel(
        RegisteredServerRecord? existingRecord,
        ServerListingTrustLevel currentTrustLevel)
    {
        if (existingRecord?.VerifiedTrustLevel is ServerListingTrustLevel existingVerifiedTrustLevel)
            return existingVerifiedTrustLevel;

        return currentTrustLevel == ServerListingTrustLevel.Official
            ? ServerListingTrustLevel.Official
            : ServerListingTrustLevel.Verified;
    }

    private static ServerListingTrustLevel DemoteForVerificationLoss(RegisteredServerRecord record)
    {
        return record.OwnerAccountId == null
            ? record.TrustLevel
            : ServerListingTrustLevel.Unverified;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private bool IsHostBanned(string? hostOrIp)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp))
            return false;

        return _hostBansByHost.ContainsKey(NormalizeHostOrValue(hostOrIp));
    }

    private static string NormalizeHostOrValue(string value)
    {
        return value.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string? TryExtractHostFromStatusEndpointUrl(string? statusEndpointUrl)
    {
        if (string.IsNullOrWhiteSpace(statusEndpointUrl))
            return null;

        return Uri.TryCreate(statusEndpointUrl, UriKind.Absolute, out Uri? uri)
            ? NormalizeHostOrValue(uri.Host)
            : null;
    }

    private static string? TryExtractHostFromConnectHint(string? connectHint)
    {
        if (string.IsNullOrWhiteSpace(connectHint))
            return null;

        return Uri.TryCreate($"tcp://{connectHint.Trim()}", UriKind.Absolute, out Uri? uri)
            ? NormalizeHostOrValue(uri.Host)
            : null;
    }

    private static string? ResolveModerationHost(RegisteredServerRecord record)
    {
        return TryExtractHostFromStatusEndpointUrl(record.AdvertisedInfo.StatusEndpointUrl)
            ?? TryExtractHostFromConnectHint(record.AdvertisedInfo.ConnectHint)
            ?? NormalizeOptional(record.PublisherFingerprint)?.ToLowerInvariant();
    }

    private static bool RecordMatchesHostBan(RegisteredServerRecord record, string bannedHost)
    {
        string normalizedHost = NormalizeHostOrValue(bannedHost);
        return StringComparer.OrdinalIgnoreCase.Equals(TryExtractHostFromStatusEndpointUrl(record.AdvertisedInfo.StatusEndpointUrl), normalizedHost) ||
               StringComparer.OrdinalIgnoreCase.Equals(TryExtractHostFromConnectHint(record.AdvertisedInfo.ConnectHint), normalizedHost) ||
               StringComparer.OrdinalIgnoreCase.Equals(NormalizeOptional(record.PublisherFingerprint), normalizedHost);
    }

    private static ServerListingTrustLevel ResolveTrustLevel(
        OfficialRegistryRegisterRequest request,
        string resolvedServerId,
        OfficialRegistryOptions options)
    {
        if (request.PublishMode != DiscoveryPublishMode.Verified)
            return ServerListingTrustLevel.Unverified;

        if (!options.VerifiedPublishTokens.TryGetValue(resolvedServerId, out string? expectedToken) ||
            string.IsNullOrWhiteSpace(request.PublishToken) ||
            !RegistryAuth.ConstantTimeEquals(expectedToken, request.PublishToken))
        {
            throw new UnauthorizedAccessException("Invalid verified publish token.");
        }

        for (int i = 0; i < options.OfficialServerIds.Length; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(options.OfficialServerIds[i], resolvedServerId))
                return ServerListingTrustLevel.Official;
        }

        return ServerListingTrustLevel.Verified;
    }

    private static ServerDiscoveryRow ToRow(RegisteredServerRecord record)
    {
        ServerAdvertisedInfo advertised = record.AdvertisedInfo;
        ServerDiscoveryStatusInfo status = record.StatusInfo;
        string? versionLabel = advertised.ContentVersion ?? advertised.ProtocolVersion;

        return new ServerDiscoveryRow(
            ProviderId: "official",
            ProviderKind: ServerDiscoveryProviderKind.Official,
            ServerId: advertised.ServerId,
            DisplayName: advertised.DisplayName,
            LeaseId: record.LeaseId,
            TrustLevel: record.TrustLevel,
            HealthState: record.HealthState,
            LifecycleState: record.LifecycleState,
            Visibility: record.Visibility,
            ConnectTargetKind: advertised.ConnectTargetKind,
            Region: advertised.Region,
            PrimaryLanguage: advertised.PrimaryLanguage,
            CurrentUsers: status.CurrentUsers,
            MaxUsers: advertised.MaxUsers,
            PeakUsers: status.PeakUsers,
            QueueLength: status.QueueLength,
            Uptime: status.Uptime,
            PasswordRequired: advertised.PasswordRequired,
            WhitelistOnly: advertised.WhitelistOnly,
            VoteCount: record.VoteCount,
            ConnectHint: advertised.ConnectHint,
            CompatibilityId: advertised.CompatibilityId,
            VersionLabel: versionLabel,
            Tags: advertised.TagsOrEmpty,
            CapabilityFlags: advertised.CapabilityFlags,
            LastSeenUtc: record.LastSeenUtc);
    }

    private ServerDiscoveryDetail ToDetail(RegisteredServerRecord record, DateTimeOffset nowUtc)
    {
        ServerAdvertisedInfo advertised = record.AdvertisedInfo;
        ServerDiscoveryStatusInfo status = record.StatusInfo;

        return new ServerDiscoveryDetail(
            Row: ToRow(record),
            Description: advertised.Description,
            Languages: advertised.LanguagesOrEmpty,
            WebsiteUrl: advertised.WebsiteUrl,
            DiscordUrl: advertised.DiscordUrl,
            IsAcceptingConnections: status.IsAcceptingConnections,
            Status: status.Status,
            Rolling24HourUsers: status.Rolling24HourUsers,
            RecentSampleCapacity: Math.Max(1, _configuredOptions.HistorySampleLimit),
            RecentSamples: record.RecentSamples,
            AggregateSamples: record.AggregateSamples,
            HistorySummary: SummarizeHistory(record.RecentSamples),
            AdvancedMetrics: SummarizeAdvancedMetrics(record, nowUtc),
            LatencySummary: SummarizeLatency(record),
            LastSuccessfulPollUtc: record.LastSuccessfulPollUtc,
            LastFailedPollUtc: record.LastFailedPollUtc,
            LastExplicitOfflineUtc: record.LastExplicitOfflineUtc,
            OwnershipClaimRequestedUtc: record.OwnershipClaimRequestedUtc,
            OwnershipVerificationError: record.OwnershipVerificationError,
            VerifiedAtUtc: record.VerifiedAtUtc);
    }

    private static OfficialRegistryAdminServerRecord ToAdminRecord(RegisteredServerRecord record)
    {
        return new OfficialRegistryAdminServerRecord(
            Row: ToRow(record),
            RegistrationId: record.RegistrationId,
            OwnerAccountId: record.OwnerAccountId,
            CreatedAtUtc: record.CreatedAtUtc,
            LastRegisterUtc: record.LastRegisterUtc,
            LastSuccessfulPollUtc: record.LastSuccessfulPollUtc,
            LastFailedPollUtc: record.LastFailedPollUtc,
            OwnershipClaimRequestedUtc: record.OwnershipClaimRequestedUtc,
            OwnershipVerificationError: record.OwnershipVerificationError,
            VerifiedAtUtc: record.VerifiedAtUtc);
    }

    private static IReadOnlyList<ServerDiscoveryHistorySample> AppendHistorySample(
        IReadOnlyList<ServerDiscoveryHistorySample>? existingSamples,
        DateTimeOffset capturedAtUtc,
        ServerDiscoveryStatusInfo statusInfo,
        ServerListingHealthState healthState,
        OfficialRegistryOptions options)
    {
        return AppendHistorySampleCore(
            existingSamples,
            capturedAtUtc,
            statusInfo,
            healthState,
            Math.Max(1, options.HistorySampleLimit),
            TimeSpan.FromSeconds(Math.Max(0, options.HistorySampleIntervalSeconds)));
    }

    private static IReadOnlyList<ServerDiscoveryHistorySample> AppendAggregateHistorySample(
        IReadOnlyList<ServerDiscoveryHistorySample>? existingSamples,
        DateTimeOffset capturedAtUtc,
        ServerDiscoveryStatusInfo statusInfo,
        ServerListingHealthState healthState,
        OfficialRegistryOptions options)
    {
        return AppendHistorySampleCore(
            existingSamples,
            capturedAtUtc,
            statusInfo,
            healthState,
            Math.Max(1, options.AggregateHistorySampleLimit),
            TimeSpan.FromSeconds(Math.Max(0, options.AggregateHistorySampleIntervalSeconds)));
    }

    private static IReadOnlyList<ServerDiscoveryHistorySample> AppendHistorySampleCore(
        IReadOnlyList<ServerDiscoveryHistorySample>? existingSamples,
        DateTimeOffset capturedAtUtc,
        ServerDiscoveryStatusInfo statusInfo,
        ServerListingHealthState healthState,
        int limit,
        TimeSpan minimumInterval)
    {
        ServerDiscoveryHistorySample next = new(
            CapturedAtUtc: capturedAtUtc,
            CurrentUsers: statusInfo.CurrentUsers,
            PeakUsers: statusInfo.PeakUsers,
            Uptime: statusInfo.Uptime,
            Status: statusInfo.Status,
            HealthState: healthState);

        // Check the skip conditions before allocating — avoids a list copy on every
        // heartbeat/poll when nothing meaningful has changed.
        if (existingSamples != null && existingSamples.Count > 0)
        {
            ServerDiscoveryHistorySample previous = existingSamples[^1];
            bool forceAppend =
                previous.HealthState != next.HealthState ||
                previous.CurrentUsers != next.CurrentUsers ||
                previous.PeakUsers != next.PeakUsers ||
                !StringComparer.Ordinal.Equals(previous.Status, next.Status);

            if (!forceAppend)
            {
                if (minimumInterval > TimeSpan.Zero && next.CapturedAtUtc - previous.CapturedAtUtc < minimumInterval)
                    return existingSamples;

                if (minimumInterval == TimeSpan.Zero && next.CapturedAtUtc == previous.CapturedAtUtc)
                    return existingSamples;
            }
        }

        List<ServerDiscoveryHistorySample> samples = existingSamples == null
            ? new List<ServerDiscoveryHistorySample>(Math.Min(limit, 4))
            : new List<ServerDiscoveryHistorySample>(existingSamples);

        samples.Add(next);
        if (samples.Count > limit)
            samples.RemoveRange(0, samples.Count - limit);

        return samples;
    }

    private void MaybeUpdateAllTimePeakPlayers(DateTimeOffset nowUtc)
    {
        int currentPlayersTotal = 0;

        foreach (RegisteredServerRecord record in _recordsByRegistrationId.Values)
        {
            if (record.LifecycleState == ServerListingLifecycleState.Active)
                currentPlayersTotal += Math.Max(0, record.StatusInfo.CurrentUsers);
        }

        if (currentPlayersTotal <= _allTimePeakPlayers)
            return;

        _allTimePeakPlayers = currentPlayersTotal;
        _allTimePeakPlayersUtcTicks = nowUtc.UtcTicks;
    }

    private static RegisteredServerRecord UpdateRecordAllTimePeak(RegisteredServerRecord record, DateTimeOffset nowUtc)
    {
        int candidatePeak = Math.Max(
            Math.Max(0, record.StatusInfo.CurrentUsers),
            Math.Max(0, record.StatusInfo.PeakUsers));

        if (candidatePeak <= record.AllTimePeakUsers)
            return record;

        return record with
        {
            AllTimePeakUsers = candidatePeak,
            AllTimePeakUsersUtc = nowUtc,
        };
    }

    private static ServerDiscoveryHistorySummary? SummarizeHistory(IReadOnlyList<ServerDiscoveryHistorySample>? samples)
    {
        if (samples == null || samples.Count == 0)
            return null;

        int onlineLikeCount = 0;
        int offlineCount = 0;
        long totalCurrentUsers = 0;
        int maxCurrentUsers = 0;
        int maxPeakUsers = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            ServerDiscoveryHistorySample sample = samples[i];
            totalCurrentUsers += sample.CurrentUsers;
            if (sample.CurrentUsers > maxCurrentUsers)
                maxCurrentUsers = sample.CurrentUsers;
            if (sample.PeakUsers > maxPeakUsers)
                maxPeakUsers = sample.PeakUsers;

            if (sample.HealthState == ServerListingHealthState.Offline)
                offlineCount++;
            else
                onlineLikeCount++;
        }

        return new ServerDiscoveryHistorySummary(
            WindowStartUtc: samples[0].CapturedAtUtc,
            WindowEndUtc: samples[^1].CapturedAtUtc,
            SampleCount: samples.Count,
            AverageCurrentUsers: totalCurrentUsers / (double)samples.Count,
            MaxCurrentUsers: maxCurrentUsers,
            MaxPeakUsers: maxPeakUsers,
            OnlineRatio: onlineLikeCount / (double)samples.Count,
            OfflineRatio: offlineCount / (double)samples.Count);
    }

    private static ServerDiscoveryAdvancedMetricsSummary? SummarizeAdvancedMetrics(
        RegisteredServerRecord record,
        DateTimeOffset nowUtc)
    {
        if ((record.AggregateSamples == null || record.AggregateSamples.Count == 0) &&
            record.AllTimePeakUsers <= 0)
        {
            return null;
        }

        IReadOnlyList<ServerDiscoveryHistorySample> samples = record.AggregateSamples ?? Array.Empty<ServerDiscoveryHistorySample>();
        WindowAggregateMetrics last24Hours = SummarizeWindowForServer(samples, nowUtc.AddHours(-24));
        WindowAggregateMetrics last7Days = SummarizeWindowForServer(samples, nowUtc.AddDays(-7));
        WindowAggregateMetrics last30Days = SummarizeWindowForServer(samples, nowUtc.AddDays(-30));

        return new ServerDiscoveryAdvancedMetricsSummary(
            PeakPlayers24Hours: last24Hours.PeakPlayers,
            AveragePlayers7Days: last7Days.AveragePlayers,
            AveragePlayers30Days: last30Days.AveragePlayers,
            OnlineRatio7Days: last7Days.OnlineRatio,
            OnlineRatio30Days: last30Days.OnlineRatio,
            AllTimePeakPlayers: record.AllTimePeakUsers,
            AllTimePeakPlayersUtc: record.AllTimePeakUsersUtc);
    }

    private static ServerDiscoveryLatencySummary? SummarizeLatency(RegisteredServerRecord record)
    {
        if (record.PollSuccessCount <= 0 && record.PollFailureCount <= 0)
            return null;

        double averageLatencyMs = record.PollSuccessCount > 0
            ? record.PollLatencyMsTotal / (double)record.PollSuccessCount
            : 0d;

        return new ServerDiscoveryLatencySummary(
            SampleCount: (int)Math.Min(int.MaxValue, record.PollSuccessCount),
            AverageLatencyMs: averageLatencyMs,
            MaxLatencyMs: record.PollLatencyMsMax,
            LastLatencyMs: record.LastPollLatencyMs,
            FailureCount: record.PollFailureCount);
    }

    public RegistryStoreRuntimeSnapshot GetRuntimeSnapshot(DateTimeOffset nowUtc)
    {
        PruneExpired(nowUtc);

        int totalRecords = 0;
        int activeRecords = 0;
        int delistedRecords = 0;
        int disabledRecords = 0;
        int onlineRecords = 0;
        int staleRecords = 0;
        int offlineRecords = 0;
        int pendingRecords = 0;
        int activeLeases = 0;
        int pollCandidates = 0;
        int visibleRows = 0;

        foreach (RegisteredServerRecord record in _recordsByRegistrationId.Values)
        {
            totalRecords++;

            switch (record.LifecycleState)
            {
                case ServerListingLifecycleState.Active:
                    activeRecords++;
                    break;
                case ServerListingLifecycleState.Delisted:
                    delistedRecords++;
                    break;
                case ServerListingLifecycleState.Disabled:
                    disabledRecords++;
                    break;
            }

            switch (record.HealthState)
            {
                case ServerListingHealthState.Online:
                    onlineRecords++;
                    break;
                case ServerListingHealthState.Stale:
                    staleRecords++;
                    break;
                case ServerListingHealthState.Offline:
                    offlineRecords++;
                    break;
                case ServerListingHealthState.Pending:
                    pendingRecords++;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(record.LeaseId))
                activeLeases++;

            if (record.LifecycleState == ServerListingLifecycleState.Active &&
                record.Visibility == ServerListingVisibility.Public &&
                !string.IsNullOrWhiteSpace(record.AdvertisedInfo.StatusEndpointUrl))
            {
                pollCandidates++;
            }

            if (record.Visibility == ServerListingVisibility.Public &&
                record.LifecycleState == ServerListingLifecycleState.Active &&
                ShouldAppearInVisibleResults(record))
            {
                visibleRows++;
            }
        }

        return new RegistryStoreRuntimeSnapshot(
            TotalRecords: totalRecords,
            ActiveRecords: activeRecords,
            DelistedRecords: delistedRecords,
            DisabledRecords: disabledRecords,
            OnlineRecords: onlineRecords,
            StaleRecords: staleRecords,
            OfflineRecords: offlineRecords,
            PendingRecords: pendingRecords,
            ActiveLeases: activeLeases,
            PollCandidates: pollCandidates,
            VisibleRows: visibleRows,
            PersistenceEnabled: _persistenceEnabled,
            PendingPersistence: HasPendingPersistence);
    }

    public RegistryAggregateMetricsSnapshot GetAggregateMetricsSnapshot(DateTimeOffset nowUtc)
    {
        PruneExpired(nowUtc);

        int currentPlayersTotal = 0;
        int totalVotes = 0;
        int serversWithHistory = 0;
        int serversWithAggregateHistory = 0;
        int serversWithLatency = 0;
        long latencyMsTotal = 0;
        long latencySampleCount = 0;
        int maxLatencyMs = 0;
        int retainedBucketIntervalSeconds = Math.Max(1, _configuredOptions.HistorySampleIntervalSeconds);
        int aggregateBucketIntervalSeconds = Math.Max(1, _configuredOptions.AggregateHistorySampleIntervalSeconds);

        Dictionary<DateTimeOffset, AggregateHistoryBucket> retainedBuckets = new();
        Dictionary<DateTimeOffset, AggregateHistoryBucket> aggregateBuckets = new();

        foreach (RegisteredServerRecord record in _recordsByRegistrationId.Values)
        {
            if (record.LifecycleState == ServerListingLifecycleState.Active)
                currentPlayersTotal += Math.Max(0, record.StatusInfo.CurrentUsers);

            totalVotes += Math.Max(0, record.VoteCount);

            if (record.RecentSamples is { Count: > 0 })
            {
                serversWithHistory++;

                foreach (ServerDiscoveryHistorySample sample in record.RecentSamples)
                {
                    DateTimeOffset bucketTimestamp = BucketTimestamp(sample.CapturedAtUtc, retainedBucketIntervalSeconds);
                    if (!retainedBuckets.TryGetValue(bucketTimestamp, out AggregateHistoryBucket bucket))
                        bucket = new AggregateHistoryBucket();

                    bucket.TotalCurrentUsers += Math.Max(0, sample.CurrentUsers);
                    bucket.SampleCount++;
                    if (sample.HealthState == ServerListingHealthState.Offline)
                        bucket.OfflineSampleCount++;
                    else
                        bucket.OnlineLikeSampleCount++;

                    retainedBuckets[bucketTimestamp] = bucket;
                }
            }

            if (record.AggregateSamples is { Count: > 0 })
            {
                serversWithAggregateHistory++;

                foreach (ServerDiscoveryHistorySample sample in record.AggregateSamples)
                {
                    DateTimeOffset bucketTimestamp = BucketTimestamp(sample.CapturedAtUtc, aggregateBucketIntervalSeconds);
                    if (!aggregateBuckets.TryGetValue(bucketTimestamp, out AggregateHistoryBucket bucket))
                        bucket = new AggregateHistoryBucket();

                    bucket.TotalCurrentUsers += Math.Max(0, sample.CurrentUsers);
                    bucket.SampleCount++;
                    if (sample.HealthState == ServerListingHealthState.Offline)
                        bucket.OfflineSampleCount++;
                    else
                        bucket.OnlineLikeSampleCount++;

                    aggregateBuckets[bucketTimestamp] = bucket;
                }
            }

            if (record.PollSuccessCount > 0)
            {
                serversWithLatency++;
                latencyMsTotal += record.PollLatencyMsTotal;
                latencySampleCount += record.PollSuccessCount;
                if (record.PollLatencyMsMax > maxLatencyMs)
                    maxLatencyMs = record.PollLatencyMsMax;
            }
        }

        DateTimeOffset? historyWindowStartUtc = null;
        DateTimeOffset? historyWindowEndUtc = null;
        double averagePlayersRetained = 0d;
        int peakPlayersRetained = 0;
        double onlineRatioRetained = 0d;
        double offlineRatioRetained = 0d;

        if (retainedBuckets.Count > 0)
        {
            long totalUsersAcrossBuckets = 0;
            long totalOnlineLikeSamples = 0;
            long totalOfflineSamples = 0;
            long totalSamples = 0;

            foreach ((DateTimeOffset bucketTimestamp, AggregateHistoryBucket bucket) in retainedBuckets)
            {
                totalUsersAcrossBuckets += bucket.TotalCurrentUsers;
                totalOnlineLikeSamples += bucket.OnlineLikeSampleCount;
                totalOfflineSamples += bucket.OfflineSampleCount;
                totalSamples += bucket.SampleCount;

                if (bucket.TotalCurrentUsers > peakPlayersRetained)
                    peakPlayersRetained = bucket.TotalCurrentUsers;

                if (!historyWindowStartUtc.HasValue || bucketTimestamp < historyWindowStartUtc.Value)
                    historyWindowStartUtc = bucketTimestamp;

                if (!historyWindowEndUtc.HasValue || bucketTimestamp > historyWindowEndUtc.Value)
                    historyWindowEndUtc = bucketTimestamp;
            }

            averagePlayersRetained = totalUsersAcrossBuckets / (double)retainedBuckets.Count;

            if (totalSamples > 0)
            {
                onlineRatioRetained = totalOnlineLikeSamples / (double)totalSamples;
                offlineRatioRetained = totalOfflineSamples / (double)totalSamples;
            }
        }

        WindowAggregateMetrics last24Hours = SummarizeWindow(aggregateBuckets, nowUtc.AddHours(-24));
        WindowAggregateMetrics last7Days = SummarizeWindow(aggregateBuckets, nowUtc.AddDays(-7));
        WindowAggregateMetrics last30Days = SummarizeWindow(aggregateBuckets, nowUtc.AddDays(-30));

        double averageLatencyMs = latencySampleCount > 0
            ? latencyMsTotal / (double)latencySampleCount
            : 0d;

        IReadOnlyList<RegistryAggregateHistoryPoint> recentAggregatePoints = aggregateBuckets
            .OrderBy(static pair => pair.Key)
            .TakeLast(24)
            .Select(static pair => new RegistryAggregateHistoryPoint(
                CapturedAtUtc: pair.Key,
                CurrentPlayersTotal: pair.Value.TotalCurrentUsers,
                OnlineRatio: pair.Value.SampleCount > 0 ? pair.Value.OnlineLikeSampleCount / (double)pair.Value.SampleCount : 0d,
                OfflineRatio: pair.Value.SampleCount > 0 ? pair.Value.OfflineSampleCount / (double)pair.Value.SampleCount : 0d,
                SampleCount: pair.Value.SampleCount))
            .ToArray();

        return new RegistryAggregateMetricsSnapshot(
            CurrentPlayersTotal: currentPlayersTotal,
            TotalVotes: totalVotes,
            ServersWithHistory: Math.Max(serversWithHistory, serversWithAggregateHistory),
            HistoryBucketCount: retainedBuckets.Count,
            HistoryWindowStartUtc: historyWindowStartUtc,
            HistoryWindowEndUtc: historyWindowEndUtc,
            AveragePlayersRetained: averagePlayersRetained,
            PeakPlayersRetained: peakPlayersRetained,
            OnlineRatioRetained: onlineRatioRetained,
            OfflineRatioRetained: offlineRatioRetained,
            ServersWithLatency: serversWithLatency,
            AverageLatencyMs: averageLatencyMs,
            MaxLatencyMs: maxLatencyMs,
            PeakPlayers24Hours: last24Hours.PeakPlayers,
            AveragePlayers7Days: last7Days.AveragePlayers,
            AveragePlayers30Days: last30Days.AveragePlayers,
            OnlineRatio7Days: last7Days.OnlineRatio,
            OnlineRatio30Days: last30Days.OnlineRatio,
            AllTimePeakPlayers: _allTimePeakPlayers,
            AllTimePeakPlayersUtc: _allTimePeakPlayersUtcTicks <= 0 ? null : new DateTimeOffset(_allTimePeakPlayersUtcTicks, TimeSpan.Zero),
            RecentAggregatePoints: recentAggregatePoints);
    }

    public bool HasPendingPersistence => _persistenceEnabled && Interlocked.Read(ref _dirtyPersistenceVersion) > Interlocked.Read(ref _persistedPersistenceVersion);
    public long CurrentQueryRevision => Interlocked.Read(ref _queryRevision);

    private static DateTimeOffset BucketTimestamp(DateTimeOffset value, int intervalSeconds)
    {
        long ticksPerBucket = TimeSpan.FromSeconds(intervalSeconds).Ticks;
        long bucketTicks = value.UtcTicks - (value.UtcTicks % ticksPerBucket);
        return new DateTimeOffset(bucketTicks, TimeSpan.Zero);
    }

    private static WindowAggregateMetrics SummarizeWindow(
        IReadOnlyDictionary<DateTimeOffset, AggregateHistoryBucket> buckets,
        DateTimeOffset cutoffUtc)
    {
        if (buckets.Count == 0)
            return default;

        long totalUsers = 0;
        long totalOnlineLikeSamples = 0;
        long totalSamples = 0;
        int peakPlayers = 0;
        int bucketCount = 0;

        foreach ((DateTimeOffset bucketTimestamp, AggregateHistoryBucket bucket) in buckets)
        {
            if (bucketTimestamp < cutoffUtc)
                continue;

            bucketCount++;
            totalUsers += bucket.TotalCurrentUsers;
            totalOnlineLikeSamples += bucket.OnlineLikeSampleCount;
            totalSamples += bucket.SampleCount;
            if (bucket.TotalCurrentUsers > peakPlayers)
                peakPlayers = bucket.TotalCurrentUsers;
        }

        if (bucketCount == 0)
            return default;

        return new WindowAggregateMetrics(
            AveragePlayers: totalUsers / (double)bucketCount,
            PeakPlayers: peakPlayers,
            OnlineRatio: totalSamples > 0 ? totalOnlineLikeSamples / (double)totalSamples : 0d);
    }

    private static WindowAggregateMetrics SummarizeWindowForServer(
        IReadOnlyList<ServerDiscoveryHistorySample> samples,
        DateTimeOffset cutoffUtc)
    {
        if (samples.Count == 0)
            return default;

        long totalUsers = 0;
        long totalOnlineLikeSamples = 0;
        int sampleCount = 0;
        int peakPlayers = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            ServerDiscoveryHistorySample sample = samples[i];
            if (sample.CapturedAtUtc < cutoffUtc)
                continue;

            sampleCount++;
            totalUsers += Math.Max(0, sample.CurrentUsers);
            if (sample.HealthState != ServerListingHealthState.Offline)
                totalOnlineLikeSamples++;
            if (sample.CurrentUsers > peakPlayers)
                peakPlayers = sample.CurrentUsers;
        }

        if (sampleCount == 0)
            return default;

        return new WindowAggregateMetrics(
            AveragePlayers: totalUsers / (double)sampleCount,
            PeakPlayers: peakPlayers,
            OnlineRatio: totalOnlineLikeSamples / (double)sampleCount);
    }

    private static IEnumerable<ServerDiscoveryRow> ApplySort(
        IEnumerable<ServerDiscoveryRow> rows,
        string? sortBy,
        bool descending)
    {
        string normalizedSort = string.IsNullOrWhiteSpace(sortBy)
            ? "name"
            : sortBy.Trim().ToLowerInvariant();

        Func<ServerDiscoveryRow, object?> keySelector = normalizedSort switch
        {
            "players" or "currentusers" => row => row.CurrentUsers ?? -1,
            "uptime" => row => row.Uptime ?? TimeSpan.Zero,
            "lastseen" => row => row.LastSeenUtc ?? DateTimeOffset.MinValue,
            "trust" => row => row.TrustLevel,
            _ => row => row.DisplayName ?? string.Empty,
        };

        if (normalizedSort is "votes" or "votecount")
        {
            return descending
                ? rows.OrderByDescending(static row => row.VoteCount)
                    .ThenByDescending(static row => row.CurrentUsers ?? -1)
                    .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(static row => row.VoteCount)
                    .ThenBy(static row => row.CurrentUsers ?? -1)
                    .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        return descending
            ? rows.OrderByDescending(keySelector).ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(keySelector).ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ShouldAppearInVisibleResults(RegisteredServerRecord record)
    {
        if (record.HealthState == ServerListingHealthState.Online ||
            record.HealthState == ServerListingHealthState.Stale)
        {
            return true;
        }

        return record.HealthState == ServerListingHealthState.Offline &&
            record.TrustLevel != ServerListingTrustLevel.Unverified;
    }

    private sealed record RegisteredServerRecord(
        string RegistrationId,
        string ServerId,
        [property: System.Text.Json.Serialization.JsonIgnore] string? LeaseId,
        [property: System.Text.Json.Serialization.JsonIgnore] string? HeartbeatToken,
        string PublisherFingerprint,
        ServerListingTrustLevel TrustLevel,
        ServerListingVisibility Visibility,
        ServerListingHealthState HealthState,
        ServerListingLifecycleState LifecycleState,
        ServerAdvertisedInfo AdvertisedInfo,
        ServerDiscoveryStatusInfo StatusInfo,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset LastRegisterUtc,
        DateTimeOffset LastPushUtc,
        DateTimeOffset? LastSeenUtc,
        DateTimeOffset? LastExplicitOfflineUtc,
        DateTimeOffset? LastSuccessfulPollUtc,
        DateTimeOffset? LastFailedPollUtc,
        DateTimeOffset? DelistedUtc,
        int ConsecutivePollFailures,
        DateTimeOffset ExpiresAtUtc,
        string? OwnerAccountId = null,
        DateTimeOffset? OwnershipClaimRequestedUtc = null,
        string? OwnershipVerificationError = null,
        string? OwnershipTokenHash = null,
        DateTimeOffset? VerifiedAtUtc = null,
        ServerListingTrustLevel VerifiedTrustLevel = ServerListingTrustLevel.Verified,
        int VoteCount = 0,
        IReadOnlyList<ServerDiscoveryHistorySample>? RecentSamples = null,
        IReadOnlyList<ServerDiscoveryHistorySample>? AggregateSamples = null,
        int AllTimePeakUsers = 0,
        DateTimeOffset? AllTimePeakUsersUtc = null,
        long PollSuccessCount = 0,
        long PollLatencyMsTotal = 0,
        int PollLatencyMsMax = 0,
        int? LastPollLatencyMs = null,
        int PollFailureCount = 0);

    private struct AggregateHistoryBucket
    {
        public int TotalCurrentUsers { get; set; }
        public int SampleCount { get; set; }
        public int OnlineLikeSampleCount { get; set; }
        public int OfflineSampleCount { get; set; }
    }

    private readonly record struct WindowAggregateMetrics(
        double AveragePlayers = 0d,
        int PeakPlayers = 0,
        double OnlineRatio = 0d);

    public sealed record RegistryPollCandidate(
        string RegistrationId,
        string ServerId,
        string StatusEndpointUrl);

    private sealed record RegistryStoreSnapshot(
        List<RegisteredServerRecord> Records,
        Dictionary<string, DateTimeOffset> LastUnverifiedRegisterByPublisher,
        List<HostBanRecord> HostBans,
        int AllTimePeakPlayers = 0,
        DateTimeOffset? AllTimePeakPlayersUtc = null);

    private void LoadState(DateTimeOffset nowUtc)
    {
        if (!_persistenceEnabled || string.IsNullOrWhiteSpace(_stateFilePath) || !File.Exists(_stateFilePath))
            return;

        string json = File.ReadAllText(_stateFilePath);
        RegistryStoreSnapshot? snapshot = JsonSerializer.Deserialize<RegistryStoreSnapshot>(json, PersistenceJsonOptions);
        if (snapshot == null)
            return;

        _recordsByRegistrationId.Clear();
        _registrationIdByServerId.Clear();
        _registrationIdByLeaseId.Clear();
        _lastUnverifiedRegisterByPublisher.Clear();
        _hostBansByHost.Clear();

        for (int i = 0; i < snapshot.Records.Count; i++)
        {
            RegisteredServerRecord record = snapshot.Records[i];
            if (!string.IsNullOrWhiteSpace(record.LeaseId) && record.ExpiresAtUtc <= nowUtc)
            {
                record = record with
                {
                    LeaseId = null,
                    HeartbeatToken = null,
                    HealthState = ServerListingHealthState.Offline,
                };
            }

            _recordsByRegistrationId[record.RegistrationId] = record;
            _registrationIdByServerId[record.ServerId] = record.RegistrationId;
            if (!string.IsNullOrWhiteSpace(record.LeaseId))
                _registrationIdByLeaseId[record.LeaseId] = record.RegistrationId;
        }

        foreach ((string publisherFingerprint, DateTimeOffset previousRegisterUtc) in snapshot.LastUnverifiedRegisterByPublisher)
            _lastUnverifiedRegisterByPublisher[publisherFingerprint] = previousRegisterUtc;

        if (snapshot.HostBans != null)
        {
            for (int i = 0; i < snapshot.HostBans.Count; i++)
            {
                HostBanRecord banRecord = snapshot.HostBans[i];
                _hostBansByHost[banRecord.Host] = banRecord;
            }
        }

        _allTimePeakPlayers = Math.Max(0, snapshot.AllTimePeakPlayers);
        _allTimePeakPlayersUtcTicks = snapshot.AllTimePeakPlayersUtc?.UtcTicks ?? 0;
    }

    public async ValueTask FlushStateIfDirtyAsync(CancellationToken cancellationToken)
    {
        if (!_persistenceEnabled || string.IsNullOrWhiteSpace(_stateFilePath))
            return;

        long dirtyVersion = Interlocked.Read(ref _dirtyPersistenceVersion);
        if (dirtyVersion <= Interlocked.Read(ref _persistedPersistenceVersion))
            return;

        if (Interlocked.CompareExchange(ref _persistenceFlushInProgress, 1, 0) != 0)
            return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            dirtyVersion = Interlocked.Read(ref _dirtyPersistenceVersion);
            if (dirtyVersion <= Interlocked.Read(ref _persistedPersistenceVersion))
                return;

            RegistryStoreSnapshot snapshot = CaptureSnapshot();
            string json = JsonSerializer.Serialize(snapshot, PersistenceJsonOptions);

            string directory = Path.GetDirectoryName(_stateFilePath)!;
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(_stateFilePath, json, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _persistedPersistenceVersion, dirtyVersion);

            stopwatch.Stop();
            _runtimeMetrics.RecordSaveState(
                (long)stopwatch.Elapsed.TotalMilliseconds,
                System.Text.Encoding.UTF8.GetByteCount(json),
                DateTimeOffset.UtcNow);
        }
        catch
        {
            stopwatch.Stop();
            _runtimeMetrics.RecordSaveStateFailure();
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _persistenceFlushInProgress, 0);
        }
    }

    private void MarkStateDirty()
    {
        if (!_persistenceEnabled)
            return;

        Interlocked.Increment(ref _dirtyPersistenceVersion);
    }

    private void MarkStoreChanged()
    {
        Interlocked.Increment(ref _queryRevision);
        MarkStateDirty();
    }

    private RegistryStoreSnapshot CaptureSnapshot()
    {
        return new RegistryStoreSnapshot(
            _recordsByRegistrationId.Values
                .OrderBy(static record => record.RegistrationId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _lastUnverifiedRegisterByPublisher.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            _hostBansByHost.Values
                .OrderBy(static record => record.Host, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _allTimePeakPlayers,
            _allTimePeakPlayersUtcTicks <= 0 ? null : new DateTimeOffset(_allTimePeakPlayersUtcTicks, TimeSpan.Zero));
    }

    private sealed record HostBanRecord(
        string Host,
        string? Reason,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset? LastAppliedAtUtc = null);
}
