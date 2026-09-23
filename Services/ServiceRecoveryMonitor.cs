public sealed class ServiceRecoveryMonitor : BackgroundService
{
    private readonly HlsProcessManager _hls;
    private readonly ILogger<ServiceRecoveryMonitor> _logger;

    public ServiceRecoveryMonitor(
        HlsProcessManager hls,
        ILogger<ServiceRecoveryMonitor> logger)
    {
        _hls = hls;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow the service and FFmpeg to start before monitoring the heartbeat.
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_hls.IsSupervisorHealthy)
            {
                consecutiveFailures = 0;
            }
            else
            {
                consecutiveFailures++;
                _logger.LogError(
                    "HLS heartbeat missing ({Count}/4). Last maintenance: {LastMaintenance}.",
                    consecutiveFailures,
                    _hls.LastMaintenanceUtc);

                if (consecutiveFailures >= 4)
                {
                    _logger.LogCritical(
                        "The HLS supervisor is unresponsive. Terminating the process so Windows Service Control Manager can restart it.");

                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    Environment.FailFast("Display server HLS supervisor heartbeat expired.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
