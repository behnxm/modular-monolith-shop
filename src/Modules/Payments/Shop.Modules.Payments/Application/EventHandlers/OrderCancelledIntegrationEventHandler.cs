using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Domain;
using Shop.Modules.Payments.Infrastructure;

namespace Shop.Modules.Payments.Application.EventHandlers;

internal sealed class OrderCancelledIntegrationEventHandler(
    PaymentsDbContext dbContext,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public async Task HandleAsync(OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.PaymentAttempts
            .FirstOrDefaultAsync(p => p.OrderId == integrationEvent.OrderId, cancellationToken);

        if (attempt is null)
        {
            // Out of order: OrderCancelled arrived before OrderPlaced.
            // Record a placeholder so that later attempts won't charge.
            var placeholder = PaymentAttempt.CreateCancelledPlaceholder(integrationEvent.OrderId);
            dbContext.PaymentAttempts.Add(placeholder);
            return;
        }

        if (attempt.Status == PaymentStatus.Succeeded)
        {
            attempt.MarkRefunded();

            var refundedEvent = new PaymentRefundedIntegrationEvent(
                attempt.OrderId,
                attempt.Id);

            dbContext.Enqueue(refundedEvent, timeProvider);
            return;
        }

        if (attempt.Status == PaymentStatus.Pending)
        {
            // Cancellation arrived before charge
            attempt.MarkFailed("Order was cancelled before payment charge");
        }
    }
}
