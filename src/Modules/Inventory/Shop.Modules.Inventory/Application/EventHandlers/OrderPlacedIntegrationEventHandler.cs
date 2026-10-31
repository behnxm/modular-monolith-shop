using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Inventory.Application.EventHandlers;

internal sealed class OrderPlacedIntegrationEventHandler(
    InventoryDbContext dbContext,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public async Task HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        // Check for idempotency or prior cancellation
        var existingReservation = await dbContext.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrderId == integrationEvent.OrderId, cancellationToken);

        if (existingReservation is not null)
        {
            // Already seen: no-op
            return;
        }

        var savepointName = "sp_res_" + integrationEvent.OrderId.ToString("N");
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(savepointName, cancellationToken);
        }

        var unavailableProductIds = new List<Guid>();

        foreach (var line in integrationEvent.Lines)
        {
            var rowsAffected = await dbContext.StockItems
                .Where(s => s.ProductId == line.ProductId && (s.OnHand - s.Reserved) >= line.Quantity)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.Reserved, x => x.Reserved + line.Quantity),
                    cancellationToken);

            if (rowsAffected == 0)
            {
                unavailableProductIds.Add(line.ProductId);
            }
        }

        if (unavailableProductIds.Count > 0)
        {
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(savepointName, cancellationToken);
            }

            var reason = $"Insufficient stock for product(s): {string.Join(", ", unavailableProductIds)}";
            var failedEvent = new StockReservationFailedIntegrationEvent(
                integrationEvent.OrderId,
                reason,
                unavailableProductIds);

            dbContext.Enqueue(failedEvent, timeProvider);
            return;
        }

        if (transaction is not null)
        {
            await transaction.ReleaseSavepointAsync(savepointName, cancellationToken);
        }

        var reservation = Reservation.Create(
            integrationEvent.OrderId,
            integrationEvent.Lines.Select(l => (l.ProductId, l.Quantity)));

        dbContext.Reservations.Add(reservation);

        var reservedEvent = new StockReservedIntegrationEvent(integrationEvent.OrderId);
        dbContext.Enqueue(reservedEvent, timeProvider);
    }
}
