using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;

namespace Shop.Modules.Ordering.Application.EventHandlers;

internal sealed class StockReservedIntegrationEventHandler(
    OrderingDbContext dbContext,
    ILogger<StockReservedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<StockReservedIntegrationEvent>
{
    public async Task HandleAsync(StockReservedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Order {OrderId} was not found when handling StockReserved", integrationEvent.OrderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            logger.LogInformation(
                "Order {OrderId} is already in status {Status} when receiving StockReserved. Ignoring.",
                order.Id,
                order.Status);
            return;
        }

        // Progress recorded; order remains Pending until PaymentSucceeded
        logger.LogInformation("Stock reserved recorded for order {OrderId}", order.Id);
    }
}
