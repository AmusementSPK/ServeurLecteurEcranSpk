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
        // Laisse le service et FFmpeg démarrer avant de surveiller le heartbeat.
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
                    "Heartbeat HLS absent ({Count}/4). Dernière maintenance: {LastMaintenance}.",
                    consecutiveFailures,
                    _hls.LastMaintenanceUtc);

                if (consecutiveFailures >= 4)
                {
                    _logger.LogCritical(
                        "Le superviseur HLS est bloqué. Arrêt volontaire du processus pour permettre au Service Control Manager de le redémarrer.");

                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    Environment.FailFast("SPK HLS supervisor heartbeat expired.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
