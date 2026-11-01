using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shop.BuildingBlocks.Infrastructure.Web;
using Shop.Modules.Payments.Application.GetPaymentAttempt;

namespace Shop.Modules.Payments.Endpoints;

internal static class PaymentsEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("api/v1/payments")
            .WithTags("Payments");

        // Read: GetPaymentAttempt (Admin only)
        group.MapGet("/{orderId:guid}", async (
            Guid orderId,
            GetPaymentAttemptHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(orderId, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblemResult(httpContext);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization("shop.admin")
        .RequireRateLimiting("read")
        .WithSummary("Get payment attempt for an order (Admin only)");
    }
}
