using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shop.BuildingBlocks.Infrastructure.Web;
using Shop.Modules.Notifications.Application.ListMyNotifications;

namespace Shop.Modules.Notifications.Endpoints;

internal static class NotificationsEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("api/v1/notifications")
            .WithTags("Notifications");

        group.MapGet(
            "",
            async (
                ListMyNotificationsHandler handler,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(cancellationToken);
                if (result.IsFailure)
                {
                    return result.Error.ToProblemResult(httpContext);
                }

                return Results.Ok(result.Value);
            })
            .RequireAuthorization("shop.customer")
            .WithName("ListMyNotifications")
            .WithSummary("List notifications for current customer");
    }
}
