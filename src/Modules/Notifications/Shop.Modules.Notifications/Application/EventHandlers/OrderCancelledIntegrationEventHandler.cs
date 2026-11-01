using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Notifications.Contracts.IntegrationEvents.V1;
using Shop.Modules.Notifications.Domain;
using Shop.Modules.Notifications.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Notifications.Application.EventHandlers;

internal sealed class OrderCancelledIntegrationEventHandler(
    NotificationsDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<OrderCancelledIntegrationEventHandler> logger)
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public async Task HandleAsync(OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var subject = "Order Cancelled";
        var body = $"Your order {integrationEvent.OrderId} was cancelled. Reason: {integrationEvent.Reason}";

        var notificationResult = Notification.Create(
            integrationEvent.CustomerId,
            NotificationKind.OrderCancelled,
            subject,
            body,
            timeProvider);

        if (notificationResult.IsFailure)
        {
            logger.LogError("Failed to create OrderCancelled notification: {Error}", notificationResult.Error);
            return;
        }

        var notification = notificationResult.Value;
        dbContext.Notifications.Add(notification);

        var recordedEvent = new NotificationRecordedIntegrationEvent(
            notification.Id,
            notification.CustomerId,
            notification.Kind.ToString());

        dbContext.Enqueue(recordedEvent, timeProvider);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
