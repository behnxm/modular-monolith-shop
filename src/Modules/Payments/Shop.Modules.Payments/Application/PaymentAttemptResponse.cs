namespace Shop.Modules.Payments.Application;

internal sealed record PaymentAttemptResponse(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? FailureReason,
    DateTimeOffset CreatedAt);
