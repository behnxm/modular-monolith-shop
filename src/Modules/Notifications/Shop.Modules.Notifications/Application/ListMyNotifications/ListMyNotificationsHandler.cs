using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Notifications.Domain;
using Shop.Modules.Notifications.Infrastructure;

namespace Shop.Modules.Notifications.Application.ListMyNotifications;

internal sealed class ListMyNotificationsHandler(
    NotificationsDbContext dbContext,
    ICurrentUser currentUser)
{
    public async Task<Result<List<NotificationResponse>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Result<List<NotificationResponse>>.Failure(
                Error.Forbidden("notifications.unauthorized", "User is not authenticated."));
        }

        var customerId = currentUser.UserId;

        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.CustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .Select(n => new NotificationResponse(
                n.Id,
                n.CustomerId,
                n.Kind.ToString(),
                n.Subject,
                n.Body,
                n.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<List<NotificationResponse>>.Success(notifications);
    }
}
