using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Domain;
using Shop.Modules.Payments.Infrastructure;

namespace Shop.Modules.Payments.Application.EventHandlers;

internal sealed class OrderPlacedIntegrationEventHandler(PaymentsDbContext dbContext)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public async Task HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var exists = await dbContext.PaymentAttempts
            .AnyAsync(p => p.OrderId == integrationEvent.OrderId, cancellationToken);

        if (exists)
        {
            // Idempotent: attempt already recorded
            return;
        }

        var attempt = PaymentAttempt.CreatePending(
            integrationEvent.OrderId,
            integrationEvent.TotalAmount,
            integrationEvent.Currency);

        dbContext.PaymentAttempts.Add(attempt);
    }
}
