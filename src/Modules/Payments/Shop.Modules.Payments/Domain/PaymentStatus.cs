namespace Shop.Modules.Payments.Domain;

internal enum PaymentStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Refunded = 4
}
