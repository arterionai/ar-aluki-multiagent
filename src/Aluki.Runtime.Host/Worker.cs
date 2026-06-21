using Aluki.Runtime.Abstractions.Skills;

namespace Aluki.Runtime.Host;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Aluki Runtime Host started. Skill contract assembly: {assembly}",
            typeof(ISkill).Assembly.GetName().Name
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Runtime heartbeat at {time}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
