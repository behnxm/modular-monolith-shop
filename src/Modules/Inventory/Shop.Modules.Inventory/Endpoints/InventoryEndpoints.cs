using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shop.BuildingBlocks.Infrastructure.Web;
using Shop.Modules.Inventory.Application.GetStock;
using Shop.Modules.Inventory.Application.ListStock;
using Shop.Modules.Inventory.Application.SetStock;

namespace Shop.Modules.Inventory.Endpoints;

internal static class InventoryEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("api/v1/inventory/stock")
            .WithTags("Inventory");

        // Write: SetStock (Admin only)
        group.MapPut("/{productId:guid}", async (
            Guid productId,
            [FromBody] SetStockRequest request,
            SetStockHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(productId, request, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.admin")
        .RequireRateLimiting("write")
        .WithSummary("Set on-hand stock for a product");

        // Read: GetStock
        group.MapGet("/{productId:guid}", async (
            Guid productId,
            GetStockHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(productId, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.customer")
        .RequireRateLimiting("read")
        .WithSummary("Get current stock level for a product");

        // Read: ListStock (Keyset pagination)
        group.MapGet("/", async (
            [FromQuery] string? cursor,
            [FromQuery] int limit,
            ListStockHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var query = new ListStockQuery(limit == 0 ? 25 : limit, cursor);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.customer")
        .RequireRateLimiting("read")
        .WithSummary("List stock items with keyset pagination");
    }
}
