using System.Text.Json;
using Aluki.Runtime.Abstractions.Governance;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.Governance;

public sealed class GovernanceRepository : IGovernanceRepository
{
    private static readonly JsonSerializerOptions _json = new();

    private readonly NpgsqlConnectionFactory _factory;
    private readonly ILogger<GovernanceRepository> _logger;

    public GovernanceRepository(NpgsqlConnectionFactory factory, ILogger<GovernanceRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    // ── Policy rules ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PolicyRule>> GetActiveRulesAsync(
        Guid tenantId, string operationType, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, rule_type, operation_type, rule_definition, priority, is_active, created_at_utc, updated_at_utc
            FROM policy_rules
            WHERE tenant_id = @tenant_id AND operation_type = @operation_type AND is_active = true
            ORDER BY priority ASC
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("operation_type", operationType);

        var rules = new List<PolicyRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rules.Add(ReadPolicyRule(reader));
        return rules;
    }

    public async Task<PolicyRule> CreateRuleAsync(CreatePolicyRuleRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO policy_rules (id, tenant_id, rule_type, operation_type, rule_definition, priority, is_active, created_at_utc, updated_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @rule_type, @operation_type, @rule_definition::jsonb, @priority, true, now(), now())
            RETURNING id, tenant_id, rule_type, operation_type, rule_definition, priority, is_active, created_at_utc, updated_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmd.Parameters.AddWithValue("rule_type", request.RuleType);
        cmd.Parameters.AddWithValue("operation_type", request.OperationType);
        cmd.Parameters.AddWithValue("rule_definition", JsonSerializer.Serialize(request.RuleDefinition, _json));
        cmd.Parameters.AddWithValue("priority", request.Priority);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadPolicyRule(reader);
    }

    public async Task<IReadOnlyList<PolicyRule>> ListRulesAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, rule_type, operation_type, rule_definition, priority, is_active, created_at_utc, updated_at_utc
            FROM policy_rules
            WHERE tenant_id = @tenant_id
            ORDER BY priority ASC, created_at_utc ASC
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        var rules = new List<PolicyRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rules.Add(ReadPolicyRule(reader));
        return rules;
    }

    // ── Policy decision log ──────────────────────────────────────────────────

    public async Task<Guid> AppendDecisionAsync(
        Guid tenantId, Guid? principalUserId, string operationType,
        string decision, string reasonCode, IReadOnlyList<string> appliedRules,
        decimal? estimatedCost, string? correlationId, object? metadata,
        CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO policy_decision_log
                (id, tenant_id, principal_user_id, operation_type, decision, reason_code,
                 applied_rules, estimated_cost, correlation_id, metadata, decided_at_utc)
            VALUES
                (gen_random_uuid(), @tenant_id, @principal_user_id, @operation_type, @decision,
                 @reason_code, @applied_rules::jsonb, @estimated_cost, @correlation_id, @metadata::jsonb, now())
            RETURNING id
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("principal_user_id", (object?)principalUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("operation_type", operationType);
        cmd.Parameters.AddWithValue("decision", decision);
        cmd.Parameters.AddWithValue("reason_code", reasonCode);
        cmd.Parameters.AddWithValue("applied_rules", JsonSerializer.Serialize(appliedRules, _json));
        cmd.Parameters.AddWithValue("estimated_cost", (object?)estimatedCost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlation_id", (object?)correlationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", metadata is null ? (object)DBNull.Value : JsonSerializer.Serialize(metadata, _json));

        var id = await cmd.ExecuteScalarAsync(ct);
        return (Guid)id!;
    }

    // ── Consent ──────────────────────────────────────────────────────────────

    public async Task<ConsentRecord?> GetActiveConsentAsync(
        Guid tenantId, Guid grantorId, Guid granteeId, string consentType, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, grantor_id, grantee_id, consent_type, granted_at_utc, revoked_at_utc, revocation_reason
            FROM consent_records
            WHERE tenant_id = @tenant_id AND grantor_id = @grantor_id AND grantee_id = @grantee_id
              AND consent_type = @consent_type AND revoked_at_utc IS NULL
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("grantor_id", grantorId);
        cmd.Parameters.AddWithValue("grantee_id", granteeId);
        cmd.Parameters.AddWithValue("consent_type", consentType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadConsentRecord(reader);
    }

    public async Task<ConsentRecord> InsertConsentAsync(GrantConsentRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO consent_records (id, tenant_id, grantor_id, grantee_id, consent_type, granted_at_utc, created_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @grantor_id, @grantee_id, @consent_type, now(), now())
            RETURNING id, tenant_id, grantor_id, grantee_id, consent_type, granted_at_utc, revoked_at_utc, revocation_reason
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmd.Parameters.AddWithValue("grantor_id", request.GrantorId);
        cmd.Parameters.AddWithValue("grantee_id", request.GranteeId);
        cmd.Parameters.AddWithValue("consent_type", request.ConsentType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadConsentRecord(reader);
    }

    public async Task<bool> RevokeConsentAsync(
        Guid tenantId, Guid grantorId, Guid granteeId, string consentType, string? reason, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE consent_records
            SET revoked_at_utc = now(), revocation_reason = @reason
            WHERE tenant_id = @tenant_id AND grantor_id = @grantor_id AND grantee_id = @grantee_id
              AND consent_type = @consent_type AND revoked_at_utc IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("grantor_id", grantorId);
        cmd.Parameters.AddWithValue("grantee_id", granteeId);
        cmd.Parameters.AddWithValue("consent_type", consentType);
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<IReadOnlyList<ConsentRecord>> ListConsentsByGrantorAsync(
        Guid tenantId, Guid grantorId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, grantor_id, grantee_id, consent_type, granted_at_utc, revoked_at_utc, revocation_reason
            FROM consent_records
            WHERE tenant_id = @tenant_id AND grantor_id = @grantor_id
            ORDER BY granted_at_utc DESC
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("grantor_id", grantorId);

        var records = new List<ConsentRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            records.Add(ReadConsentRecord(reader));
        return records;
    }

    public async Task<IReadOnlyList<ConsentRecord>> ListConsentsByGranteeAsync(
        Guid tenantId, Guid granteeId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, grantor_id, grantee_id, consent_type, granted_at_utc, revoked_at_utc, revocation_reason
            FROM consent_records
            WHERE tenant_id = @tenant_id AND grantee_id = @grantee_id
            ORDER BY granted_at_utc DESC
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("grantee_id", granteeId);

        var records = new List<ConsentRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            records.Add(ReadConsentRecord(reader));
        return records;
    }

    // ── Read helpers ─────────────────────────────────────────────────────────

    private static PolicyRule ReadPolicyRule(NpgsqlDataReader r)
    {
        var defJson = r.GetString(4);
        var def = JsonSerializer.Deserialize<Dictionary<string, object?>>(defJson, _json)
                  ?? new Dictionary<string, object?>();
        return new PolicyRule(
            Id: r.GetGuid(0),
            TenantId: r.GetGuid(1),
            RuleType: r.GetString(2),
            OperationType: r.GetString(3),
            RuleDefinition: def,
            Priority: r.GetInt32(5),
            IsActive: r.GetBoolean(6),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(7),
            UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(8));
    }

    private static ConsentRecord ReadConsentRecord(NpgsqlDataReader r)
        => new(
            Id: r.GetGuid(0),
            TenantId: r.GetGuid(1),
            GrantorId: r.GetGuid(2),
            GranteeId: r.GetGuid(3),
            ConsentType: r.GetString(4),
            GrantedAtUtc: r.GetFieldValue<DateTimeOffset>(5),
            RevokedAtUtc: r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6),
            RevocationReason: r.IsDBNull(7) ? null : r.GetString(7));
}
