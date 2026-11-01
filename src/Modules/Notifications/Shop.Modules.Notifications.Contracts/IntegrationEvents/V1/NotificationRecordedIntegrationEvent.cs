using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Notifications.Contracts.IntegrationEvents.V1;

[IntegrationEventName("notifications.notification-recorded.v1")]
public sealed record NotificationRecordedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public NotificationRecordedIntegrationEvent(
        Guid id,
        DateTimeOffset occurredOn,
        Guid notificationId,
        string customerId,
        string kind)
    {
        Id = id;
        OccurredOn = occurredOn;
        NotificationId = notificationId;
        CustomerId = customerId;
        Kind = kind;
    }

    public NotificationRecordedIntegrationEvent(
        Guid notificationId,
        string customerId,
        string kind)
        : this(Guid.NewGuid(), DateTimeOffset.UtcNow, notificationId, customerId, kind)
    {
    }

    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
    public Guid NotificationId { get; init; }
    public string CustomerId { get; init; }
    public string Kind { get; init; }
}
