using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;

namespace Shop.Modules.Ordering.Application.GetOrder;

internal sealed class GetOrderHandler(OrderingDbContext dbContext, ICurrentUser currentUser)
{
    public async Task<Result<OrderResponse>> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return Result<OrderResponse>.Failure(OrderingErrors.OrderNotFound(orderId));
        }

        // BOLA enforcement: another customer's order returns 404
        if (!currentUser.HasScope("shop.admin") && order.CustomerId != currentUser.UserId)
        {
            return Result<OrderResponse>.Failure(OrderingErrors.OrderNotFound(orderId));
        }

        var lines = order.Lines
            .Select(l => new OrderLineResponse(l.ProductId, l.ProductNameSnapshot, l.UnitPriceSnapshot, l.Quantity, l.LineTotal))
            .ToList();

        return Result<OrderResponse>.Success(
            new OrderResponse(
                order.Id,
                order.CustomerId,
                order.Status.ToString(),
                lines,
                order.TotalAmount,
                order.Currency,
                order.CancellationReason,
                order.CreatedAt));
    }
}
