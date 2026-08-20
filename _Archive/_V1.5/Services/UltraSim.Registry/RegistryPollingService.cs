using System.Net.Http.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using UltraSim.Discovery;

namespace UltraSim.Registry;

public sealed class RegistryPollingService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly InMemoryOfficialRegistryStore _store;
    private readonly OfficialRegistryOptions _options;
    private readonly ILogger<RegistryPollingService> _logger;
    private readonly RegistryRuntimeMetrics _runtimeMetrics;

    public RegistryPollingService(
        IHttpClientFactory httpClientFactory,
        InMemoryOfficialRegistryStore store,
        IOptions<OfficialRegistryOptions> options,
        ILogger<RegistryPollingService> logger,
        RegistryRuntimeMetrics runtimeMetrics)
    {
        _httpClientFactory = httpClientFactory;
        _store = store;
        _options = options.Value;
        _logger = logger;
        _runtimeMetrics = runtimeMetrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_options.EnablePolling)
            {
                try
                {
                    await PollOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Registry polling loop failed.");
                }
            }

            try
            {
                _store.RunLifecycleSweep(_options, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Registry lifecycle sweep failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<InMemoryOfficialRegistryStore.RegistryPollCandidate> candidates = _store.GetPollCandidates(DateTimeOffset.UtcNow);
        if (candidates.Count == 0)
        {
            _runtimeMetrics.RecordPollRound(0, 0, 0, 0, DateTimeOffset.UtcNow);
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using HttpClient client = _httpClientFactory.CreateClient("registry-poller");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.PollTimeoutSeconds));
        int maxDegreeOfParallelism = Math.Max(1, _options.PollMaxConcurrency);
        int successCount = 0;
        int failureCount = 0;

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
            },
            async (candidate, ct) =>
            {
                try
                {
                    Stopwatch requestStopwatch = Stopwatch.StartNew();
                    ServerDiscoveryPollStatusResponse? response = await client.GetFromJsonAsync<ServerDiscoveryPollStatusResponse>(
                        candidate.StatusEndpointUrl,
                        ct).ConfigureAwait(false);
                    requestStopwatch.Stop();

                    DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                    if (response == null)
                    {
                        _store.ApplyPollFailure(candidate.RegistrationId, nowUtc, _options.PollFailureThreshold);
                        Interlocked.Increment(ref failureCount);
                        return;
                    }

                    _store.ApplyPollSuccess(candidate.RegistrationId, response, nowUtc, (int)Math.Max(0, requestStopwatch.ElapsedMilliseconds));
                    Interlocked.Increment(ref successCount);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Polling failed for server {ServerId} at {StatusEndpointUrl}: {ExceptionType}: {Message}",
                        candidate.ServerId,
                        candidate.StatusEndpointUrl,
                        ex.GetType().Name,
                        ex.Message);
                    _store.ApplyPollFailure(candidate.RegistrationId, DateTimeOffset.UtcNow, _options.PollFailureThreshold);
                    Interlocked.Increment(ref failureCount);
                }
            }).ConfigureAwait(false);

        stopwatch.Stop();
        _runtimeMetrics.RecordPollRound(
            candidates.Count,
            successCount,
            failureCount,
            (long)stopwatch.Elapsed.TotalMilliseconds,
            DateTimeOffset.UtcNow);
    }
}
