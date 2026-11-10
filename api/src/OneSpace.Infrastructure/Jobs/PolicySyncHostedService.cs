using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSpace.Application.Policies;

namespace OneSpace.Infrastructure.Jobs;

public sealed class PolicySyncOptions
{
    public const string SectionName = "PolicySync";

    public bool Enabled { get; set; } = true;

    /// <summary>PRD Open Item 3: daily by default; shorten if policies change often.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    public bool RunOnStartup { get; set; } = true;
}

/// <summary>FR-3 scheduled sync of ComplyKaro policies into the search cache.</summary>
public sealed partial class PolicySyncHostedService(
    IServiceScopeFactory scopes,
    IOptions<PolicySyncOptions> options,
    TimeProvider clock,
    ILogger<PolicySyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled) return;

        if (o.RunOnStartup) await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(o.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SyncPoliciesUseCase>().ExecuteAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep serving the last good cache; the next tick retries.
            LogSyncFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Policy sync failed; keeping the existing cache")]
    private static partial void LogSyncFailed(ILogger logger, Exception ex);
}
