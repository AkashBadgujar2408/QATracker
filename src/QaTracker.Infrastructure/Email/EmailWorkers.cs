using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Notifications;

namespace QaTracker.Infrastructure.Email;

/// <summary>Delivers the outbox every <see cref="EmailOptions.SendIntervalSeconds"/> (ADR-029).</summary>
internal sealed class EmailOutboxWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> options, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SendIntervalSeconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().SendDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "E-mail outbox run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Queues the daily summary at <see cref="EmailOptions.DigestTimeIst"/> IST. After a restart it catches up a slot it
/// missed (last run before the latest slot).
/// </summary>
internal sealed class DailyDigestWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> options, IClock clock, ILogger<DailyDigestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var time = options.Value.DigestTime;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var digest = scope.ServiceProvider.GetRequiredService<DailyDigestHandler>();
                    var latest = DailyDigestHandler.LatestSlot(clock.UtcNow, time);
                    if (await digest.LastRunAsync(stoppingToken) is not { } last || last < latest)
                    {
                        var sent = await digest.HandleAsync(stoppingToken);
                        logger.LogInformation("Daily digest queued {Count} mails", sent);
                    }
                }

                var next = DailyDigestHandler.LatestSlot(clock.UtcNow, time).AddDays(1);
                var wait = next - clock.UtcNow;
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Daily digest failed; retrying in 10 minutes");
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }
    }
}
