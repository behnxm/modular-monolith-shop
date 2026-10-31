using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shop.BuildingBlocks.Infrastructure.Web;
using Shop.Modules.Catalog.Application.CreateProduct;
using Shop.Modules.Catalog.Application.DeactivateProduct;
using Shop.Modules.Catalog.Application.GetProduct;
using Shop.Modules.Catalog.Application.ListProducts;
using Shop.Modules.Catalog.Application.UpdateProduct;

namespace Shop.Modules.Catalog.Endpoints;

internal static class CatalogEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("api/v1/catalog/products")
            .WithTags("Catalog");

        // Write: CreateProduct (Admin only)
        group.MapPost("/", async (
            [FromBody] CreateProductRequest request,
            CreateProductHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Created($"/api/v1/catalog/products/{result.Value.Id}", result.Value);
        })
        .RequireAuthorization("shop.admin")
        .RequireRateLimiting("write")
        .WithSummary("Create a new product in the catalog");

        // Write: UpdateProduct (Admin only)
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateProductRequest request,
            UpdateProductHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.NoContent();
        })
        .RequireAuthorization("shop.admin")
        .RequireRateLimiting("write")
        .WithSummary("Update product details");

        // Write: DeactivateProduct (Admin only)
        group.MapDelete("/{id:guid}", async (
            Guid id,
            DeactivateProductHandler handler,
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
        .RequireAuthorization("shop.admin")
        .RequireRateLimiting("write")
        .WithSummary("Deactivate a product");

        // Read: GetProduct (Customer or Admin)
        group.MapGet("/{id:guid}", async (
            Guid id,
            GetProductHandler handler,
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
        .WithSummary("Get a single product by ID");

        // Read: ListProducts (Customer or Admin)
        group.MapGet("/", async (
            [FromQuery] string? cursor,
            [FromQuery] int limit,
            [FromQuery] string? search,
            ListProductsHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var query = new ListProductsQuery(cursor, limit == 0 ? 25 : limit, search);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.customer")
        .RequireRateLimiting("read")
        .WithSummary("List products with keyset pagination and search");
    }
}
