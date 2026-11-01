namespace Shop.Modules.Notifications.Application;

internal sealed record NotificationResponse(
    Guid Id,
    string CustomerId,
    string Kind,
    string Subject,
    string Body,
    DateTimeOffset CreatedAt);
