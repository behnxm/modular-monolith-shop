using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Web;
using Shop.Modules.Ordering.Application;
using Shop.Modules.Ordering.Application.CancelOrder;
using Shop.Modules.Ordering.Application.GetOrder;
using Shop.Modules.Ordering.Application.ListAllOrders;
using Shop.Modules.Ordering.Application.ListMyOrders;
using Shop.Modules.Ordering.Application.PlaceOrder;

namespace Shop.Modules.Ordering.Endpoints;

internal static class OrderingEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("api/v1/ordering/orders")
            .WithTags("Orders");

        // Write: PlaceOrder (Customer)
        group.MapPost("/", async (
            [FromBody] PlaceOrderRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            PlaceOrderHandler handler,
            ICurrentUser currentUser,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64)
            {
                var keyError = Error.Validation(
                    "ordering.missing_idempotency_key",
                    "The 'Idempotency-Key' request header is required and must not exceed 64 characters.");
                return keyError.ToProblemResult(httpContext);
            }

            var result = await handler.HandleAsync(currentUser.UserId, idempotencyKey, request, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            if (result.Value.IsReplayed)
            {
                httpContext.Response.Headers.Append("Idempotent-Replayed", "true");
            }

            return Results.Accepted($"/api/v1/ordering/orders/{result.Value.Order.Id}", result.Value.Order);
        })
        .RequireAuthorization("shop.customer")
        .RequireRateLimiting("write")
        .WithSummary("Place a new order");

        // Read: ListMyOrders (Customer)
        group.MapGet("/", async (
            [FromQuery] string? cursor,
            [FromQuery] int? limit,
            ListMyOrdersHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var query = new ListOrdersQuery(limit ?? 25, cursor);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.customer")
        .RequireRateLimiting("read")
        .WithSummary("List the current customer's orders");

        // Read: Admin ListAllOrders
        group.MapGet("/admin", async (
            [FromQuery] string? cursor,
            [FromQuery] int? limit,
            ListAllOrdersHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var query = new ListOrdersQuery(limit ?? 25, cursor);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.admin")
        .RequireRateLimiting("read")
        .WithSummary("Admin list all orders");

        // Read: GetOrder
        group.MapGet("/{id:guid}", async (
            Guid id,
            GetOrderHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.customer")
        .RequireRateLimiting("read")
        .WithSummary("Get an order by ID");

        // Write: CancelOrder (Customer or Admin)
        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            CancelOrderHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.NoContent();
        })
        .RequireAuthorization("shop.customer")
        .RequireRateLimiting("write")
        .WithSummary("Cancel a pending order");
    }
}
