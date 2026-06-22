namespace Aluki.Runtime.Abstractions.Billing;

public interface IBillingCycleService
{
    Task<Invoice> GenerateInvoiceAsync(GenerateInvoiceRequest request, CancellationToken ct);
    Task<CreditBalance> TopUpCreditAsync(TopUpCreditRequest request, CancellationToken ct);
}
