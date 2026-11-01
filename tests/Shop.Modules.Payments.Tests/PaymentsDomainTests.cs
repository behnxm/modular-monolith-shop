using Microsoft.Extensions.Configuration;
using Shop.Modules.Payments.Domain;
using Shop.Modules.Payments.Infrastructure;
using Shouldly;
using Xunit;

namespace Shop.Modules.Payments.Tests;

public sealed class PaymentsDomainTests
{
    [Fact]
    public void CreatePending_ValidParameters_InitializesCorrectly()
    {
        var orderId = Guid.CreateVersion7();
        var attempt = PaymentAttempt.CreatePending(orderId, 150.00m, "USD");

        attempt.OrderId.ShouldBe(orderId);
        attempt.Amount.ShouldBe(150.00m);
        attempt.Currency.ShouldBe("USD");
        attempt.Status.ShouldBe(PaymentStatus.Pending);
        attempt.FailureReason.ShouldBeNull();
    }

    [Fact]
    public void CreatePending_NegativeOrZeroAmount_ThrowsArgumentOutOfRangeException()
    {
        var orderId = Guid.CreateVersion7();
        Should.Throw<ArgumentOutOfRangeException>(() => PaymentAttempt.CreatePending(orderId, -1m));
        Should.Throw<ArgumentOutOfRangeException>(() => PaymentAttempt.CreatePending(orderId, 0m));
    }

    [Fact]
    public void Transitions_HappyPath_PendingToSucceededToRefunded()
    {
        var attempt = PaymentAttempt.CreatePending(Guid.CreateVersion7(), 100m);

        var successResult = attempt.MarkSucceeded();
        successResult.IsSuccess.ShouldBeTrue();
        attempt.Status.ShouldBe(PaymentStatus.Succeeded);

        // Re-marking Succeeded is idempotent
        attempt.MarkSucceeded().IsSuccess.ShouldBeTrue();

        var refundResult = attempt.MarkRefunded();
        refundResult.IsSuccess.ShouldBeTrue();
        attempt.Status.ShouldBe(PaymentStatus.Refunded);

        // Re-refunding is idempotent
        attempt.MarkRefunded().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Transitions_FailedPath_PendingToFailed()
    {
        var attempt = PaymentAttempt.CreatePending(Guid.CreateVersion7(), 100m);

        var failResult = attempt.MarkFailed("Card expired");
        failResult.IsSuccess.ShouldBeTrue();
        attempt.Status.ShouldBe(PaymentStatus.Failed);
        attempt.FailureReason.ShouldBe("Card expired");

        // Re-failing is idempotent
        attempt.MarkFailed("Another reason").IsSuccess.ShouldBeTrue();

        // Cannot succeed from Failed
        attempt.MarkSucceeded().IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Transitions_Refund_CannotRefundPendingPayment()
    {
        var attempt = PaymentAttempt.CreatePending(Guid.CreateVersion7(), 100m);

        var result = attempt.MarkRefunded();
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payments.invalid_transition");
    }

    [Fact]
    public async Task FakePaymentGateway_ApprovesUnderOrEqualLimit_AndDeclinesOverLimit()
    {
        var config = new ConfigurationBuilder().Build();
        var gateway = new FakePaymentGateway(config);

        // Under limit ($10,000)
        var underResult = await gateway.ChargeAsync(Guid.CreateVersion7(), 500.00m, "USD", CancellationToken.None);
        underResult.IsSuccess.ShouldBeTrue();
        underResult.TransactionId.ShouldNotBeNull();
        underResult.ErrorReason.ShouldBeNull();

        // Exactly at limit ($10,000)
        var atLimitResult = await gateway.ChargeAsync(Guid.CreateVersion7(), 10000.00m, "USD", CancellationToken.None);
        atLimitResult.IsSuccess.ShouldBeTrue();

        // Over limit ($10,000.01)
        var overResult = await gateway.ChargeAsync(Guid.CreateVersion7(), 10000.01m, "USD", CancellationToken.None);
        overResult.IsSuccess.ShouldBeFalse();
        overResult.TransactionId.ShouldBeNull();
        overResult.ErrorReason.ShouldNotBeNull();
        overResult.ErrorReason.ShouldContain("exceeds maximum approval limit");
    }
}
