using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Eventing;
using Shop.BuildingBlocks.Infrastructure.Telemetry;

namespace Shop.BuildingBlocks.Infrastructure.Inbox;

public sealed class InboxDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<InboxDispatcher> logger) : IInboxDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxConcurrencyRetries = 3;

    public async Task DispatchAsync(
        IntegrationEventEnvelope envelope,
        IntegrationEventHandlerRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(registration);

        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await ExecuteDeliveryAsync(envelope, registration, cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt <= MaxConcurrencyRetries)
            {
                logger.LogWarning(
                    ex,
                    "Concurrency conflict dispatching {ContractName} to handler {HandlerName} on attempt {Attempt}. Retrying...",
                    registration.ContractName,
                    registration.HandlerName,
                    attempt);

                await Task.Delay(TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt)), cancellationToken);
            }
        }
    }

    private async Task ExecuteDeliveryAsync(
        IntegrationEventEnvelope envelope,
        IntegrationEventHandlerRegistration registration,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(registration.DbContextType);
        var schema = dbContext.Schema;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var sql = $"INSERT INTO \"{schema}\".\"inbox_messages\" (message_id, handler_name, processed_on) VALUES (@p0, @p1, @p2) ON CONFLICT DO NOTHING;";

        var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
            sql,
            [envelope.Id, registration.HandlerName, now],
            cancellationToken);

        if (rowsAffected == 0)
        {
            // Idempotent deduplication: Already processed by this handler
            logger.LogDebug(
                "Duplicate message {MessageId} skipped for handler {HandlerName}",
                envelope.Id,
                registration.HandlerName);

            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        // Trace context propagation: start child activity
        ActivityContext parentContext = default;
        if (!string.IsNullOrEmpty(envelope.TraceParent) &&
            ActivityContext.TryParse(envelope.TraceParent, envelope.TraceState, out var parsedContext))
        {
            parentContext = parsedContext;
        }

        using var activity = ShopTelemetry.ActivitySource.StartActivity(
            $"Handle {registration.ContractName}",
            ActivityKind.Consumer,
            parentContext);

        activity?.SetTag("messaging.system", "inprocess");
        activity?.SetTag("messaging.destination", registration.ContractName);
        activity?.SetTag("module.name", schema);
        activity?.SetTag("module.handler", registration.HandlerName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var handlerInstance = scope.ServiceProvider.GetRequiredService(registration.HandlerType);
            var eventInstance = JsonSerializer.Deserialize(envelope.Payload, registration.EventType, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize payload for {registration.ContractName}");

            var handleMethod = registration.HandlerType.GetMethod("HandleAsync", [registration.EventType, typeof(CancellationToken)])
                ?? throw new InvalidOperationException($"Method HandleAsync not found on {registration.HandlerType.FullName}");

            var task = (Task)handleMethod.Invoke(handlerInstance, [eventInstance, cancellationToken])!;
            await task;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            stopwatch.Stop();
            ShopTelemetry.EventsHandledTotal.Add(
                1,
                new KeyValuePair<string, object?>("module", schema),
                new KeyValuePair<string, object?>("handler", registration.HandlerName),
                new KeyValuePair<string, object?>("result", "success"));

            ShopTelemetry.EventHandleDurationSeconds.Record(
                stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("module", schema),
                new KeyValuePair<string, object?>("handler", registration.HandlerName));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await transaction.RollbackAsync(cancellationToken);

            ShopTelemetry.EventsHandledTotal.Add(
                1,
                new KeyValuePair<string, object?>("module", schema),
                new KeyValuePair<string, object?>("handler", registration.HandlerName),
                new KeyValuePair<string, object?>("result", "failure"));

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var exceptionToThrow = (ex is System.Reflection.TargetInvocationException tie && tie.InnerException is not null)
                ? tie.InnerException
                : ex;

            logger.LogError(
                exceptionToThrow,
                "Handler {HandlerName} failed processing event {ContractName} (MessageId: {MessageId}). Rolled back.",
                registration.HandlerName,
                registration.ContractName,
                envelope.Id);

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exceptionToThrow).Throw();
        }
    }
}
