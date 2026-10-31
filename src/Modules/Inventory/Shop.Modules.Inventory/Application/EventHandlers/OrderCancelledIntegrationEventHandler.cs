using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Inventory.Application.EventHandlers;

internal sealed class OrderCancelledIntegrationEventHandler(InventoryDbContext dbContext)
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public async Task HandleAsync(OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == integrationEvent.OrderId, cancellationToken);

        if (reservation is null)
        {
            // Out-of-order arrival: OrderCancelled arrived before OrderPlaced.
            // Record a released placeholder so a later OrderPlaced will not reserve stock.
            dbContext.Reservations.Add(Reservation.CreateReleasedPlaceholder(integrationEvent.OrderId));
            return;
        }

        if (reservation.Status != ReservationStatus.Reserved)
        {
            // Already released, committed, or in a terminal state: idempotent no-op
            return;
        }

        foreach (var line in reservation.Lines)
        {
            await dbContext.StockItems
                .Where(s => s.ProductId == line.ProductId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.Reserved, x => x.Reserved - line.Quantity),
                    cancellationToken);
        }

        reservation.Release();
    }
}
