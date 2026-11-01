using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Notifications.Contracts.IntegrationEvents.V1;
using Shop.Modules.Notifications.Domain;
using Shop.Modules.Notifications.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Notifications.Application.EventHandlers;

internal sealed class OrderConfirmedIntegrationEventHandler(
    NotificationsDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<OrderConfirmedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var subject = "Order Confirmed";
        var body = $"Your order {integrationEvent.OrderId} for {integrationEvent.TotalAmount:F2} {integrationEvent.Currency} has been confirmed.";

        var notificationResult = Notification.Create(
            integrationEvent.CustomerId,
            NotificationKind.OrderConfirmed,
            subject,
            body,
            timeProvider);

        if (notificationResult.IsFailure)
        {
            logger.LogError("Failed to create OrderConfirmed notification: {Error}", notificationResult.Error);
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
