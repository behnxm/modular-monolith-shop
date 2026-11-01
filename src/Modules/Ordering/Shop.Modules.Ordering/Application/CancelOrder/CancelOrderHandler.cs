using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;

namespace Shop.Modules.Ordering.Application.CancelOrder;

internal sealed class CancelOrderHandler(
    OrderingDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure(OrderingErrors.OrderNotFound(orderId));
        }

        // BOLA check: another customer's order returns 404
        if (!currentUser.HasScope("shop.admin") && order.CustomerId != currentUser.UserId)
        {
            return Result.Failure(OrderingErrors.OrderNotFound(orderId));
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            // Idempotent
            return Result.Success();
        }

        var cancelResult = order.Cancel("Cancelled by customer request");
        if (cancelResult.IsFailure)
        {
            return cancelResult;
        }

        var cancelledEvent = new OrderCancelledIntegrationEvent(
            order.Id,
            order.CustomerId,
            order.CancellationReason ?? "Cancelled by customer");

        dbContext.Enqueue(cancelledEvent, timeProvider);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
