using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;
using Shop.Modules.Payments.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Ordering.Application.EventHandlers;

internal sealed class PaymentSucceededIntegrationEventHandler(
    OrderingDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<PaymentSucceededIntegrationEventHandler> logger)
    : IIntegrationEventHandler<PaymentSucceededIntegrationEvent>
{
    public async Task HandleAsync(PaymentSucceededIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Order {OrderId} was not found when handling PaymentSucceeded", integrationEvent.OrderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            logger.LogInformation(
                "Order {OrderId} is already in status {Status} when receiving PaymentSucceeded. Ignoring.",
                order.Id,
                order.Status);
            return;
        }

        var confirmResult = order.Confirm();
        if (confirmResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to confirm order {OrderId}: {Error}",
                order.Id,
                confirmResult.Error.Description);
            return;
        }

        var confirmedEvent = new OrderConfirmedIntegrationEvent(
            order.Id,
            order.CustomerId,
            order.TotalAmount,
            order.Currency);

        dbContext.Enqueue(confirmedEvent, timeProvider);
    }
}
