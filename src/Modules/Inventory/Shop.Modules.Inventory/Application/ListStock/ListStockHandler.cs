using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;

namespace Shop.Modules.Inventory.Application.ListStock;

internal sealed class ListStockHandler(InventoryDbContext dbContext)
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 25;

    public async Task<Result<PagedStockResponse>> HandleAsync(
        ListStockQuery query,
        CancellationToken cancellationToken)
    {
        var limit = query.Limit switch
        {
            <= 0 => DefaultLimit,
            > MaxLimit => MaxLimit,
            _ => query.Limit
        };

        Guid? cursorProductId = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            if (!TryDecodeCursor(query.Cursor, out var parsedId))
            {
                return Result<PagedStockResponse>.Failure(
                    Error.Validation("inventory.invalid_cursor", "The provided cursor is invalid or malformed."));
            }

            cursorProductId = parsedId;
        }

        var dbQuery = dbContext.StockItems.AsNoTracking().AsQueryable();

        if (cursorProductId.HasValue)
        {
            var cid = cursorProductId.Value;
            dbQuery = dbQuery.Where(s => s.ProductId > cid);
        }

        var items = await dbQuery
            .OrderBy(s => s.ProductId)
            .Take(limit + 1)
            .Select(s => new StockResponse(s.ProductId, s.OnHand, s.Reserved, s.OnHand - s.Reserved))
            .ToListAsync(cancellationToken);

        var hasNextPage = items.Count > limit;
        if (hasNextPage)
        {
            items.RemoveAt(items.Count - 1);
        }

        string? nextCursor = null;
        if (hasNextPage && items.Count > 0)
        {
            var lastItem = items[^1];
            nextCursor = EncodeCursor(lastItem.ProductId);
        }

        return Result<PagedStockResponse>.Success(
            new PagedStockResponse(items, nextCursor, hasNextPage));
    }

    private static string EncodeCursor(Guid productId)
    {
        var bytes = Encoding.UTF8.GetBytes(productId.ToString());
        return WebEncoders.Base64UrlEncode(bytes);
    }

    private static bool TryDecodeCursor(string cursor, out Guid productId)
    {
        productId = Guid.Empty;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            var str = Encoding.UTF8.GetString(bytes);
            return Guid.TryParse(str, out productId);
        }
        catch
        {
            return false;
        }
    }
}
