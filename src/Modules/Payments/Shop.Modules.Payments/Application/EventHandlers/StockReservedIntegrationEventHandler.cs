using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Domain;
using Shop.Modules.Payments.Infrastructure;

namespace Shop.Modules.Payments.Application.EventHandlers;

internal sealed class StockReservedIntegrationEventHandler(
    PaymentsDbContext dbContext,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider,
    ILogger<StockReservedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<StockReservedIntegrationEvent>
{
    public async Task HandleAsync(StockReservedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.PaymentAttempts
            .FirstOrDefaultAsync(p => p.OrderId == integrationEvent.OrderId, cancellationToken);

        if (attempt is null)
        {
            logger.LogWarning("Payment attempt for order {OrderId} not found when handling StockReserved", integrationEvent.OrderId);
            return;
        }

        if (attempt.Status != PaymentStatus.Pending)
        {
            logger.LogInformation(
                "Payment attempt for order {OrderId} is already decided (Status: {Status}). Will not charge again.",
                attempt.OrderId,
                attempt.Status);
            return;
        }

        var gatewayResult = await paymentGateway.ChargeAsync(
            attempt.OrderId,
            attempt.Amount,
            attempt.Currency,
            cancellationToken);

        if (gatewayResult.IsSuccess)
        {
            attempt.MarkSucceeded();

            var succeededEvent = new PaymentSucceededIntegrationEvent(
                attempt.OrderId,
                attempt.Id,
                attempt.Amount,
                attempt.Currency);

            dbContext.Enqueue(succeededEvent, timeProvider);
        }
        else
        {
            var reason = gatewayResult.ErrorReason ?? "Payment declined by payment gateway";
            attempt.MarkFailed(reason);

            var failedEvent = new PaymentFailedIntegrationEvent(
                attempt.OrderId,
                reason);

            dbContext.Enqueue(failedEvent, timeProvider);
        }
    }
}
