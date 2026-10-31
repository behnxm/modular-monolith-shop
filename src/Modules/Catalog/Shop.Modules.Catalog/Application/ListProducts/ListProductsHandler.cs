using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;

namespace Shop.Modules.Catalog.Application.ListProducts;

internal sealed class ListProductsHandler(CatalogDbContext dbContext)
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 25;

    public async Task<Result<PagedProductsResponse>> HandleAsync(
        ListProductsQuery query,
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
            var cursorDecoded = TryDecodeCursor(query.Cursor, out var parsedCreatedAt, out var parsedId);
            if (!cursorDecoded)
            {
                return Result<PagedProductsResponse>.Failure(CatalogErrors.InvalidCursor);
            }

            cursorCreatedAt = parsedCreatedAt;
            cursorId = parsedId;
        }

        var dbQuery = dbContext.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            var skuResult = Sku.Create(search);
            if (skuResult.IsSuccess)
            {
                var targetSku = skuResult.Value;
                dbQuery = dbQuery.Where(p => EF.Functions.ILike(p.Name, $"{search}%") || p.Sku == targetSku);
            }
            else
            {
                dbQuery = dbQuery.Where(p => EF.Functions.ILike(p.Name, $"{search}%"));
            }
        }

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var cTime = cursorCreatedAt.Value;
            var cId = cursorId.Value;
            dbQuery = dbQuery.Where(p => p.CreatedAt > cTime || (p.CreatedAt == cTime && p.Id > cId));
        }

        var fetched = await dbQuery
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(limit + 1)
            .Select(p => new ProductResponse(
                p.Id,
                p.Sku.Value,
                p.Name,
                p.Description,
                p.Price,
                p.Currency,
                p.IsActive,
                p.CreatedAt))
            .ToListAsync(cancellationToken);

        string? nextCursor = null;
        IReadOnlyList<ProductResponse> items;

        if (fetched.Count > limit)
        {
            items = fetched.Take(limit).ToList();
            var lastItem = items[^1];
            nextCursor = EncodeCursor(lastItem.CreatedAt, lastItem.Id);
        }
        else
        {
            items = fetched;
        }

        return Result<PagedProductsResponse>.Success(new PagedProductsResponse(items, nextCursor));
    }

    public static string EncodeCursor(DateTimeOffset createdAt, Guid id)
    {
        var raw = $"{createdAt.UtcTicks}_{id}";
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecodeCursor(string cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            var decoded = Encoding.UTF8.GetString(bytes);
            var parts = decoded.Split('_');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var parsedId))
            {
                return false;
            }

            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            id = parsedId;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
