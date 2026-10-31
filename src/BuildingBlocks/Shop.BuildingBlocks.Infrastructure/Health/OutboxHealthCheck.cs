using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shop.BuildingBlocks.Infrastructure.Data;

namespace Shop.BuildingBlocks.Infrastructure.Health;

public sealed class OutboxHealthCheck<TContext>(
    TContext dbContext,
    TimeProvider timeProvider) : IHealthCheck
    where TContext : ModuleDbContext
{
    private static readonly TimeSpan DefaultBacklogThreshold = TimeSpan.FromMinutes(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var schema = dbContext.Schema;
        var now = timeProvider.GetUtcNow();

        var parkedCount = await dbContext.OutboxMessages
            .CountAsync(m => m.FailedOn != null, cancellationToken);

        var oldestUnprocessed = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOn == null && m.FailedOn == null)
            .OrderBy(m => m.OccurredOn)
            .Select(m => (DateTimeOffset?)m.OccurredOn)
            .FirstOrDefaultAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["module"] = schema,
            ["parked_count"] = parkedCount,
            ["oldest_unprocessed"] = oldestUnprocessed?.ToString("o") ?? "none"
        };

        if (parkedCount > 0)
        {
            return HealthCheckResult.Degraded(
                $"Module '{schema}' has {parkedCount} parked (failed) outbox messages.",
                data: data);
        }

        if (oldestUnprocessed.HasValue && (now - oldestUnprocessed.Value) > DefaultBacklogThreshold)
        {
            var lag = now - oldestUnprocessed.Value;
            return HealthCheckResult.Degraded(
                $"Module '{schema}' outbox backlog is degraded. Oldest message is {lag.TotalSeconds:F0}s old.",
                data: data);
        }

        return HealthCheckResult.Healthy($"Module '{schema}' outbox is healthy.", data: data);
    }
}
