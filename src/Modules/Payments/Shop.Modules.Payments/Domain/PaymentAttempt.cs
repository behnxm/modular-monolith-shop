using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Payments.Domain;

internal sealed class PaymentAttempt
{
    private PaymentAttempt()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; internal set; }

    public static PaymentAttempt CreatePending(Guid orderId, decimal amount, string currency = "USD")
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        }

        return new PaymentAttempt
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static PaymentAttempt CreateCancelledPlaceholder(Guid orderId)
    {
        return new PaymentAttempt
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Amount = 0,
            Currency = "USD",
            Status = PaymentStatus.Failed,
            FailureReason = "Order was cancelled prior to payment initialization",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public Result MarkSucceeded()
    {
        if (Status == PaymentStatus.Succeeded)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.Pending)
        {
            return Result.Failure(
                Error.Conflict("payments.invalid_transition", $"Cannot mark payment as succeeded from status '{Status}'."));
        }

        Status = PaymentStatus.Succeeded;
        FailureReason = null;
        return Result.Success();
    }

    public Result MarkFailed(string reason)
    {
        if (Status == PaymentStatus.Failed)
        {
            return Result.Success();
        }

        if (Status is PaymentStatus.Succeeded or PaymentStatus.Refunded)
        {
            return Result.Failure(
                Error.Conflict("payments.invalid_transition", $"Cannot mark payment as failed from status '{Status}'."));
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        return Result.Success();
    }

    public Result MarkRefunded()
    {
        if (Status == PaymentStatus.Refunded)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.Succeeded)
        {
            return Result.Failure(
                Error.Conflict("payments.invalid_transition", $"Cannot refund payment in status '{Status}'. Only succeeded payments can be refunded."));
        }

        Status = PaymentStatus.Refunded;
        return Result.Success();
    }
}
