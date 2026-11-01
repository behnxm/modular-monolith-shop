namespace Shop.Modules.Payments.Infrastructure;

internal sealed record PaymentGatewayResult(bool IsSuccess, string? TransactionId, string? ErrorReason);

internal interface IPaymentGateway
{
    Task<PaymentGatewayResult> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken);
}
