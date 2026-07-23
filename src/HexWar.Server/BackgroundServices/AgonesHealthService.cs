namespace HexWar.Server.BackgroundServices;

using Agones;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

public class AgonesHealthService : BackgroundService
{
    private readonly IAgonesSDK _agones;
    private readonly ILogger<AgonesHealthService> _logger;
    private readonly TimeSpan _healthInterval = TimeSpan.FromSeconds(2);

    public AgonesHealthService(IAgonesSDK agones, ILogger<AgonesHealthService> logger)
    {
        _agones = agones;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agones Health Check service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _agones.HealthAsync();
                _logger.LogDebug("Agones Health ping sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending Agones Health ping.");
            }

            await Task.Delay(_healthInterval, stoppingToken);
        }

        _logger.LogInformation("Agones Health Check service stopped.");
    }
}
