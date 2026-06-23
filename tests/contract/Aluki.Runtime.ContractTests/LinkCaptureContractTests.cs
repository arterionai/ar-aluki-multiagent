using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for SB-009A Link Capture pure-logic utilities (canonicalization,
/// URL extraction, hash stability). These tests exercise only stateless helpers in
/// Aluki.Runtime.Abstractions and do not require a database or any real service.
/// </summary>
[Trait("Category", "Contract")]
public sealed class LinkCaptureContractTests
{
    // ── TryCanonical ─────────────────────────────────────────────────────────

    [Fact]
    public void Canonical_strips_fragment()
    {
        var result = LinkCanonicalization.TryCanonical("https://example.com/page#section");

        Assert.NotNull(result);
        Assert.DoesNotContain("#", result);
        Assert.Equal("https://example.com/page", result);
    }

    [Fact]
    public void Canonical_lowercases_scheme_and_host()
    {
        var result = LinkCanonicalization.TryCanonical("HTTP://EXAMPLE.COM/path");

        Assert.NotNull(result);
        Assert.StartsWith("http://example.com/", result);
    }

    [Fact]
    public void Canonical_rejects_relative_url()
    {
        var result = LinkCanonicalization.TryCanonical("/relative");

        Assert.Null(result);
    }

    [Fact]
    public void Canonical_rejects_ftp_scheme()
    {
        var result = LinkCanonicalization.TryCanonical("ftp://example.com/file.txt");

        Assert.Null(result);
    }

    // ── ComputeHash ───────────────────────────────────────────────────────────

    [Fact]
    public void Hash_is_stable_for_same_input()
    {
        var canonical = "https://example.com/page";

        var hash1 = LinkCanonicalization.ComputeHash(canonical);
        var hash2 = LinkCanonicalization.ComputeHash(canonical);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_differs_for_different_urls()
    {
        var hash1 = LinkCanonicalization.ComputeHash("https://example.com/a");
        var hash2 = LinkCanonicalization.ComputeHash("https://example.com/b");

        Assert.NotEqual(hash1, hash2);
    }

    // ── ExtractUrls ───────────────────────────────────────────────────────────

    [Fact]
    public void ExtractUrls_finds_http_url_in_text()
    {
        var urls = LinkCanonicalization.ExtractUrls("Check out https://example.com today");

        Assert.Single(urls);
        Assert.Equal("https://example.com", urls[0]);
    }

    [Fact]
    public void ExtractUrls_finds_multiple_urls()
    {
        var urls = LinkCanonicalization.ExtractUrls(
            "First https://example.com second http://other.org/page");

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://example.com", urls);
        Assert.Contains("http://other.org/page", urls);
    }

    [Fact]
    public void ExtractUrls_ignores_non_urls()
    {
        var urls = LinkCanonicalization.ExtractUrls("ftp://files.example.com /relative www.example.com");

        Assert.Empty(urls);
    }

    [Fact]
    public void ExtractUrls_returns_empty_for_plain_text()
    {
        var urls = LinkCanonicalization.ExtractUrls("No links in this message at all");

        Assert.Empty(urls);
    }
}

// ── Stubs (referenced here for completeness; not exercised in contract tests) ──

/// <summary>
/// Repository stub that throws — ensures contract tests never reach the database.
/// </summary>
internal sealed class ThrowingLinkCaptureRepository : ILinkCaptureRepository
{
    private static T Throw<T>() =>
        throw new InvalidOperationException("Contract tests must not reach the repository.");

    public Task<(Guid ArtifactId, bool IsNew)> UpsertArtifactAsync(LinkArtifactRecord record, CancellationToken ct) => Throw<Task<(Guid, bool)>>();
    public Task<bool> TryAddProvenanceAsync(LinkProvenanceRecord record, CancellationToken ct) => Throw<Task<bool>>();
    public Task<LinkArtifactRecord?> GetArtifactByHashAsync(Guid tenantId, string urlHash, CancellationToken ct) => Throw<Task<LinkArtifactRecord?>>();
    public Task<IReadOnlyList<LinkArtifactRecord>> SearchArtifactsAsync(Guid tenantId, Guid contextScopeId, string query, int limit, CancellationToken ct) => Throw<Task<IReadOnlyList<LinkArtifactRecord>>>();
    public Task<LinkProvenanceRecord?> GetFirstProvenanceAsync(Guid tenantId, Guid artifactId, CancellationToken ct) => Throw<Task<LinkProvenanceRecord?>>();
    public Task UpdateEnrichmentAsync(Guid artifactId, string status, string? reasonCode, string descriptionText, string? siteName, string? titleText, CancellationToken ct) => Throw<Task>();
    public Task<Guid> CreateConfirmationAsync(PendingConfirmationRecord record, CancellationToken ct) => Throw<Task<Guid>>();
    public Task<PendingConfirmationRecord?> GetActivePendingAsync(Guid tenantId, string sessionId, string conversationId, CancellationToken ct) => Throw<Task<PendingConfirmationRecord?>>();
    public Task<bool> TryConsumeConfirmationAsync(Guid confirmationId, string newState, Guid resolvedByPrincipalId, string resolveMessageId, string resolveCause, DateTimeOffset resolvedAt, CancellationToken ct) => Throw<Task<bool>>();
    public Task<int> ExpireStaleConfirmationsAsync(DateTimeOffset now, CancellationToken ct) => Throw<Task<int>>();
    public Task<Guid> RecordEnrichmentAttemptAsync(Guid tenantId, Guid artifactId, int attemptNo, DateTimeOffset startedAt, CancellationToken ct) => Throw<Task<Guid>>();
    public Task CompleteEnrichmentAttemptAsync(Guid attemptId, string outcome, string? reasonCode, int durationMs, DateTimeOffset completedAt, CancellationToken ct) => Throw<Task>();
    public Task RecordPolicyDecisionAsync(Guid tenantId, Guid artifactId, string decision, string reasonCode, string destinationHost, CancellationToken ct) => Throw<Task>();
    public Task WriteAuditAsync(Guid tenantId, string entityType, Guid entityId, string eventType, string actorType, string? actorId, object payload, CancellationToken ct) => Throw<Task>();
}

/// <summary>
/// Policy evaluator stub that throws — ensures contract tests never reach enrichment policy.
/// </summary>
internal sealed class ThrowingLinkEnrichmentPolicyEvaluator : ILinkEnrichmentPolicyEvaluator
{
    public PolicyEvaluationResult Evaluate(string canonicalUrl) =>
        throw new InvalidOperationException("Contract tests must not reach the policy evaluator.");
}
