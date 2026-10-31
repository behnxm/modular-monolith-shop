using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Eventing;
using Shop.BuildingBlocks.Infrastructure.Telemetry;

namespace Shop.BuildingBlocks.Infrastructure.Outbox;

public class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IIntegrationEventTransport transport,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : ModuleDbContext
{
    private readonly OutboxProcessorOptions _options = ResolveOptions(configuration);

    private static OutboxProcessorOptions ResolveOptions(IConfiguration configuration)
    {
        var options = new OutboxProcessorOptions();
        // Extract module name from TContext name (e.g., CatalogDbContext -> catalog)
        var contextName = typeof(TContext).Name;
        var moduleName = contextName.EndsWith("DbContext", StringComparison.Ordinal)
            ? contextName[..^9].ToLowerInvariant()
            : contextName.ToLowerInvariant();

        configuration.GetSection($"Modules:{moduleName}:Outbox").Bind(options);
        return options;
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var schema = db.Schema;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var sql = $"""
            SELECT * FROM "{schema}"."outbox_messages"
            WHERE processed_on IS NULL AND failed_on IS NULL AND next_attempt_on <= @p0
            ORDER BY occurred_on
            LIMIT @p1
            FOR UPDATE SKIP LOCKED;
            """;

        var messages = await db.OutboxMessages
            .FromSqlRaw(sql, now, _options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var successCount = 0;
        foreach (var message in messages)
        {
            ActivityContext parentContext = default;
            if (!string.IsNullOrEmpty(message.TraceParent) &&
                ActivityContext.TryParse(message.TraceParent, message.TraceState, out var parsed))
            {
                parentContext = parsed;
            }

            using var activity = ShopTelemetry.ActivitySource.StartActivity(
                $"Publish {message.ContractName}",
                ActivityKind.Producer,
                parentContext);

            activity?.SetTag("messaging.system", "inprocess");
            activity?.SetTag("messaging.destination", message.ContractName);
            activity?.SetTag("module.name", schema);

            var envelope = new IntegrationEventEnvelope(
                message.Id,
                message.ContractName,
                message.Payload,
                message.OccurredOn,
                message.TraceParent,
                message.TraceState);

            try
            {
                await transport.PublishAsync(envelope, cancellationToken);
                message.ProcessedOn = timeProvider.GetUtcNow();
                message.LastError = null;
                successCount++;

                ShopTelemetry.EventsPublishedTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("module", schema),
                    new KeyValuePair<string, object?>("event", message.ContractName));
            }
            catch (Exception ex)
            {
                message.Attempts++;
                var jitter = Random.Shared.Next(50, 250);
                var delay = TimeSpan.FromSeconds(Math.Pow(2, message.Attempts)) + TimeSpan.FromMilliseconds(jitter);
                message.NextAttemptOn = timeProvider.GetUtcNow() + delay;
                message.LastError = ex.ToString();

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                if (message.Attempts >= _options.MaxAttempts)
                {
                    message.FailedOn = timeProvider.GetUtcNow();
                    ShopTelemetry.OutboxFailedTotal.Add(1, new KeyValuePair<string, object?>("module", schema));

                    logger.LogError(
                        ex,
                        "Outbox message {MessageId} ({ContractName}) in module {Schema} exceeded max attempts ({MaxAttempts}) and is parked.",
                        message.Id,
                        message.ContractName,
                        schema,
                        _options.MaxAttempts);
                }
                else
                {
                    logger.LogWarning(
                        ex,
                        "Outbox message {MessageId} ({ContractName}) in module {Schema} failed attempt {Attempt}. Retrying at {NextAttemptOn}.",
                        message.Id,
                        message.ContractName,
                        schema,
                        message.Attempts,
                        message.NextAttemptOn);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return successCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMs), timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in OutboxProcessor for {Context}", typeof(TContext).Name);
                await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMs), timeProvider, stoppingToken);
            }
        }
    }
}
