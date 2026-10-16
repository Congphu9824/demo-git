using ECommerce.Domain.Enums;

namespace ECommerce.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "CreditCard"; // CreditCard, DebitCard, DigitalWallet, BankTransfer
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string TransactionId { get; set; } = string.Empty;
    public string Provider { get; set; } = "SimulatedStripeGateway";
    public string? ErrorMessage { get; set; }
    public double DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
