using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Inventory.Application.EventHandlers;

internal sealed class OrderConfirmedIntegrationEventHandler(InventoryDbContext dbContext)
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == integrationEvent.OrderId, cancellationToken);

        if (reservation is null || reservation.Status == ReservationStatus.Committed)
        {
            // Idempotent: nothing to commit or already committed
            return;
        }

        if (reservation.Status != ReservationStatus.Reserved)
        {
            return;
        }

        foreach (var line in reservation.Lines)
        {
            await dbContext.StockItems
                .Where(s => s.ProductId == line.ProductId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.OnHand, x => x.OnHand - line.Quantity)
                          .SetProperty(x => x.Reserved, x => x.Reserved - line.Quantity),
                    cancellationToken);
        }

        reservation.Commit();
    }
}
