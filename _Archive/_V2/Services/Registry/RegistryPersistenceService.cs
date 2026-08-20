using Microsoft.Extensions.Options;

namespace UltraSim.Registry;

public sealed class RegistryPersistenceService : BackgroundService
{
    private readonly InMemoryOfficialRegistryStore _store;
    private readonly OfficialRegistryOptions _options;
    private readonly ILogger<RegistryPersistenceService> _logger;

    public RegistryPersistenceService(
        InMemoryOfficialRegistryStore store,
        IOptions<OfficialRegistryOptions> options,
        ILogger<RegistryPersistenceService> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableStatePersistence)
            return;

        TimeSpan interval = TimeSpan.FromMilliseconds(Math.Max(100, _options.PersistenceFlushIntervalMilliseconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.FlushStateIfDirtyAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Registry persistence flush failed.");
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

        try
        {
            await _store.FlushStateIfDirtyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Final registry persistence flush failed.");
        }
    }
}
