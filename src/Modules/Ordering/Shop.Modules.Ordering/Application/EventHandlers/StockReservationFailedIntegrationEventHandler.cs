using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;

namespace Shop.Modules.Ordering.Application.EventHandlers;

internal sealed class StockReservationFailedIntegrationEventHandler(
    OrderingDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<StockReservationFailedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<StockReservationFailedIntegrationEvent>
{
    public async Task HandleAsync(StockReservationFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Order {OrderId} was not found when handling StockReservationFailed", integrationEvent.OrderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            logger.LogInformation(
                "Order {OrderId} is already in status {Status} when receiving StockReservationFailed. Ignoring.",
                order.Id,
                order.Status);
            return;
        }

        var cancelResult = order.Cancel(integrationEvent.Reason);
        if (cancelResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to cancel order {OrderId}: {Error}",
                order.Id,
                cancelResult.Error.Description);
            return;
        }

        var cancelledEvent = new OrderCancelledIntegrationEvent(
            order.Id,
            order.CustomerId,
            integrationEvent.Reason);

        dbContext.Enqueue(cancelledEvent, timeProvider);
    }
}
