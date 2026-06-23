namespace Aluki.Runtime.Abstractions.Billing;

public interface IEntitlementService
{
    Task<UsageRecordResult> RecordUsageAsync(RecordUsageRequest request, CancellationToken ct);
    Task<EntitlementSnapshot> GetEntitlementSnapshotAsync(Guid tenantId, CancellationToken ct);
}
