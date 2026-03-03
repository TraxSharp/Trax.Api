using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;

namespace Trax.Api.Services.HealthCheck;

/// <summary>
/// ASP.NET Core health check that reports Trax scheduler system health from DB queries.
/// </summary>
public class TraxHealthCheck(IDataContextProviderFactory dataContextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var queueDepth = await db
            .WorkQueues.AsNoTracking()
            .CountAsync(w => w.Status == WorkQueueStatus.Queued, ct);

        var inProgress = await db
            .Metadatas.AsNoTracking()
            .CountAsync(m => m.TrainState == TrainState.InProgress, ct);

        var cutoff = DateTime.UtcNow.AddHours(-1);
        var recentFailed = await db
            .Metadatas.AsNoTracking()
            .CountAsync(m => m.TrainState == TrainState.Failed && m.EndTime > cutoff, ct);

        var deadLetters = await db
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.Status == DeadLetterStatus.AwaitingIntervention, ct);

        var data = new Dictionary<string, object>
        {
            ["queueDepth"] = queueDepth,
            ["inProgress"] = inProgress,
            ["failedLastHour"] = recentFailed,
            ["deadLetters"] = deadLetters,
        };

        if (deadLetters > 0 || recentFailed > 10)
            return HealthCheckResult.Degraded(
                "Elevated failures or unresolved dead letters",
                data: data
            );

        return HealthCheckResult.Healthy("All systems operational", data: data);
    }
}
