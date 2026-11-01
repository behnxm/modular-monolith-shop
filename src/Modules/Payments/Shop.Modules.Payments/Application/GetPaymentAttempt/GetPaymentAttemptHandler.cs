using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Payments.Infrastructure;

namespace Shop.Modules.Payments.Application.GetPaymentAttempt;

internal sealed class GetPaymentAttemptHandler(PaymentsDbContext dbContext)
{
    public async Task<Result<PaymentAttemptResponse>> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.PaymentAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

        if (attempt is null)
        {
            return Result<PaymentAttemptResponse>.Failure(
                Error.NotFound("payments.attempt_not_found", $"Payment attempt for order '{orderId}' was not found."));
        }

        return Result<PaymentAttemptResponse>.Success(
            new PaymentAttemptResponse(
                attempt.Id,
                attempt.OrderId,
                attempt.Amount,
                attempt.Currency,
                attempt.Status.ToString(),
                attempt.FailureReason,
                attempt.CreatedAt));
    }
}
