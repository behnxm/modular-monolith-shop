using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Notifications.Domain;

internal sealed class Notification
{
    private Notification()
    {
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Notification> Create(
        string customerId,
        NotificationKind kind,
        string subject,
        string body,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Result<Notification>.Failure(NotificationsErrors.EmptyCustomerId);
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Result<Notification>.Failure(NotificationsErrors.EmptySubject);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result<Notification>.Failure(NotificationsErrors.EmptyBody);
        }

        var now = timeProvider.GetUtcNow();
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(now),
            CustomerId = customerId.Trim(),
            Kind = kind,
            Subject = subject.Trim(),
            Body = body.Trim(),
            CreatedAt = now
        };

        return Result<Notification>.Success(notification);
    }
}
