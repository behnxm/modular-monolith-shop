using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Notifications.Domain;

internal static class NotificationsErrors
{
    public static readonly Error EmptyCustomerId =
        Error.Validation("notifications.empty_customer_id", "Customer ID cannot be empty.");

    public static readonly Error EmptySubject =
        Error.Validation("notifications.empty_subject", "Notification subject cannot be empty.");

    public static readonly Error EmptyBody =
        Error.Validation("notifications.empty_body", "Notification body cannot be empty.");
}
