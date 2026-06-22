using Aluki.Runtime.Abstractions.Governance;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Host.Skills.Governance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class GovernanceIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public GovernanceIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    private (IGovernanceRepository repo, IPolicyDecisionEngine engine, IConsentManager consent) BuildServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = _fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<NpgsqlConnectionFactory>();
        services.AddScoped<GovernanceRepository>();
        services.AddScoped<IGovernanceRepository>(sp => sp.GetRequiredService<GovernanceRepository>());
        services.AddScoped<ConsentManager>();
        services.AddScoped<IConsentManager>(sp => sp.GetRequiredService<ConsentManager>());
        services.AddScoped<PolicyDecisionEngine>();
        services.AddScoped<IPolicyDecisionEngine>(sp => sp.GetRequiredService<PolicyDecisionEngine>());
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IGovernanceRepository>(),
                sp.GetRequiredService<IPolicyDecisionEngine>(),
                sp.GetRequiredService<IConsentManager>());
    }

    [Fact]
    public async Task Policy_rule_create_and_list_roundtrip()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (repo, _, _) = BuildServices();

        var request = new CreatePolicyRuleRequest(
            seeded.TenantId,
            PolicyRuleType.FeatureFlag,
            "test.operation",
            new Dictionary<string, object?> { ["enabled"] = true },
            Priority: 50);

        var created = await repo.CreateRuleAsync(request, CancellationToken.None);
        var list = await repo.ListRulesAsync(seeded.TenantId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(PolicyRuleType.FeatureFlag, created.RuleType);
        Assert.Contains(list, r => r.Id == created.Id);
    }

    [Fact]
    public async Task Policy_evaluation_deny_decision_is_logged()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (repo, engine, _) = BuildServices();

        await repo.CreateRuleAsync(new CreatePolicyRuleRequest(
            seeded.TenantId, PolicyRuleType.FeatureFlag, "blocked.op",
            new Dictionary<string, object?> { ["enabled"] = false }), CancellationToken.None);

        var decision = await engine.EvaluateAsync(
            new PolicyEvaluationRequest(seeded.TenantId, seeded.UserId, "blocked.op"),
            CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Deny, decision.Decision);
        Assert.Equal(PolicyReasonCode.FeatureDisabled, decision.ReasonCode);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Consent_grant_revoke_check_roundtrip()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (_, _, consentManager) = BuildServices();
        var granteeId = Guid.NewGuid();

        var granted = await consentManager.GrantAsync(
            new GrantConsentRequest(seeded.TenantId, seeded.UserId, granteeId, ConsentType.ShareMemory),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, granted.Id);
        Assert.True(granted.IsActive);

        var checkBefore = await consentManager.CheckAsync(
            seeded.TenantId, seeded.UserId, granteeId, ConsentType.ShareMemory, CancellationToken.None);
        Assert.True(checkBefore);

        var revoked = await consentManager.RevokeAsync(
            new RevokeConsentRequest(seeded.TenantId, seeded.UserId, granteeId, ConsentType.ShareMemory, "test revocation"),
            CancellationToken.None);
        Assert.True(revoked);

        var checkAfter = await consentManager.CheckAsync(
            seeded.TenantId, seeded.UserId, granteeId, ConsentType.ShareMemory, CancellationToken.None);
        Assert.False(checkAfter);
    }

    [Fact]
    public async Task Consent_grant_is_idempotent()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (_, _, consentManager) = BuildServices();
        var granteeId = Guid.NewGuid();

        var first = await consentManager.GrantAsync(
            new GrantConsentRequest(seeded.TenantId, seeded.UserId, granteeId, ConsentType.ViewCalendar),
            CancellationToken.None);
        var second = await consentManager.GrantAsync(
            new GrantConsentRequest(seeded.TenantId, seeded.UserId, granteeId, ConsentType.ViewCalendar),
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task List_consents_by_grantor_returns_records()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (_, _, consentManager) = BuildServices();
        var granteeId = Guid.NewGuid();

        await consentManager.GrantAsync(
            new GrantConsentRequest(seeded.TenantId, seeded.UserId, granteeId, ConsentType.DelegatedReminderSend),
            CancellationToken.None);

        var list = await consentManager.ListGrantedByAsync(seeded.TenantId, seeded.UserId, CancellationToken.None);

        Assert.Contains(list, c => c.GranteeId == granteeId && c.ConsentType == ConsentType.DelegatedReminderSend);
    }
}
