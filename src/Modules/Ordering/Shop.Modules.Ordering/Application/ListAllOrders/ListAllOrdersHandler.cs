using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Ordering.Infrastructure;

namespace Shop.Modules.Ordering.Application.ListAllOrders;

internal sealed class ListAllOrdersHandler(OrderingDbContext dbContext)
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 25;

    public async Task<Result<PagedOrdersResponse>> HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken)
    {
        var limit = query.Limit switch
        {
            <= 0 => DefaultLimit,
            > MaxLimit => MaxLimit,
            _ => query.Limit
        };

        DateTimeOffset? cursorCreatedAt = null;
        Guid? cursorId = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            if (!OrderCursorHelper.TryDecodeCursor(query.Cursor, out var parsedCreated, out var parsedId))
            {
                return Result<PagedOrdersResponse>.Failure(
                    Error.Validation("ordering.invalid_cursor", "The provided cursor is invalid or malformed."));
            }

            cursorCreatedAt = parsedCreated;
            cursorId = parsedId;
        }

        var dbQuery = dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .AsQueryable();

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var cTime = cursorCreatedAt.Value;
            var cId = cursorId.Value;
            dbQuery = dbQuery.Where(o => o.CreatedAt < cTime || (o.CreatedAt == cTime && o.Id < cId));
        }

        var orders = await dbQuery
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasNextPage = orders.Count > limit;
        if (hasNextPage)
        {
            orders.RemoveAt(orders.Count - 1);
        }

        string? nextCursor = null;
        if (hasNextPage && orders.Count > 0)
        {
            var last = orders[^1];
            nextCursor = OrderCursorHelper.EncodeCursor(last.CreatedAt, last.Id);
        }

        var responses = orders.Select(o => new OrderResponse(
            o.Id,
            o.CustomerId,
            o.Status.ToString(),
            o.Lines.Select(l => new OrderLineResponse(l.ProductId, l.ProductNameSnapshot, l.UnitPriceSnapshot, l.Quantity, l.LineTotal)).ToList(),
            o.TotalAmount,
            o.Currency,
            o.CancellationReason,
            o.CreatedAt)).ToList();

        return Result<PagedOrdersResponse>.Success(new PagedOrdersResponse(responses, nextCursor, hasNextPage));
    }
}
