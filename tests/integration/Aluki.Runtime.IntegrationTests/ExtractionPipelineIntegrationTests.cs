using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Extraction;
using Aluki.Runtime.Extraction.Configuration;
using Aluki.Runtime.Extraction.Persistence;
using Aluki.Runtime.Extraction.Policies;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Extraction.Security;
using Aluki.Runtime.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-004 US1/US2: end-to-end extraction lifecycle against scoped PostgreSQL with
/// AI-independent fake providers. Verifies job persistence, confidence tiering,
/// status transitions, provenance, and idempotency. Skipped unless
/// ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class ExtractionPipelineIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public ExtractionPipelineIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Text_extraction_persists_job_result_fields_and_completes_with_warnings()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var coordinator = BuildCoordinator();

        var request = TextRequest(seed, "Reunión con Ana el martes para cerrar el contrato.", extractionId: Guid.NewGuid().ToString("N"));
        var result = await coordinator.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<ExtractionResponse>(result.Body);
        // The fake provider returns a low-confidence fact ⇒ completed_with_warnings.
        Assert.Equal(ExtractionJobStatus.CompletedWithWarnings, response.JobStatus);
        Assert.NotNull(response.ExtractionResults);
        Assert.Equal(ExtractionType.TextSummary, response.ExtractionResults!.ExtractionType);

        // Low-confidence field is withheld from the surfaced set (no fabrication).
        Assert.DoesNotContain(response.ExtractionResults.ExtractedFields, f => f.ConfidenceTier == ConfidenceTier.Low);

        // Persistence: job, result, and the FULL field set (incl. low) are stored.
        Assert.Equal("completed_with_warnings", await JobStatus(seed.TenantId, response.JobId));
        Assert.Equal(1, await CountResults(seed.TenantId, response.JobId));
        Assert.True(await CountFields(seed.TenantId, response.JobId) >= 3); // summary + high + low
        Assert.True(await CountAudit(seed.TenantId, response.JobId, ExtractionAuditEventType.ExtractionCompleted) >= 1);
    }

    [Fact]
    public async Task Status_endpoint_returns_persisted_lifecycle_state()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var coordinator = BuildCoordinator();
        var submit = await coordinator.ProcessAsync(TextRequest(seed, "Nota corta."), CancellationToken.None);
        var jobId = Assert.IsType<ExtractionResponse>(submit.Body).JobId;

        var status = await coordinator.GetStatusAsync(
            Principal(seed), seed.TenantId, jobId, "c-status", CancellationToken.None);

        Assert.Equal(200, status.StatusCode);
        var body = Assert.IsType<JobStatusResponse>(status.Body);
        Assert.Equal(jobId, body.JobId);
        Assert.Equal(100.0, body.CompletionPct);
    }

    [Fact]
    public async Task Duplicate_submission_is_idempotent_same_job()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var coordinator = BuildCoordinator();
        var extractionId = Guid.NewGuid().ToString("N");

        var first = Assert.IsType<ExtractionResponse>((await coordinator.ProcessAsync(TextRequest(seed, "x", extractionId), CancellationToken.None)).Body);
        var second = Assert.IsType<ExtractionResponse>((await coordinator.ProcessAsync(TextRequest(seed, "x", extractionId), CancellationToken.None)).Body);

        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(1, await CountJobs(seed.TenantId, extractionId));
    }

    [Fact]
    public async Task Image_input_returns_not_implemented_and_fails_job()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var coordinator = BuildCoordinator();
        var request = new ExtractionRequest(
            Guid.NewGuid().ToString("N"), seed.TenantId, "c-img", Principal(seed),
            new ExtractionInput(ExtractionInputType.Image, "upload", null, "jpg", null, null,
                ImageData: Convert.ToBase64String([1, 2, 3]), ImageType: "receipt"),
            null);

        var result = await coordinator.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(501, result.StatusCode);
        var response = Assert.IsType<ExtractionResponse>(result.Body);
        Assert.Equal(ExtractionJobStatus.Failed, response.JobStatus);
        Assert.Equal("failed", await JobStatus(seed.TenantId, response.JobId));
    }

    private ExtractionCoordinator BuildCoordinator()
    {
        var factory = BuildFactory(_fixture.ConnectionString!);
        return new ExtractionCoordinator(
            new ExtractionScopeGuard(factory),
            new ExtractionStore(factory),
            new FakeTranscriptionProvider(),
            new FakeTextExtractionProvider(),
            Options.Create(new ExtractionOptions()),
            NullLogger<ExtractionCoordinator>.Instance);
    }

    private static ExtractionRequest TextRequest(SeededPrincipal seed, string text, string? extractionId = null) =>
        new(extractionId, seed.TenantId, "c1", Principal(seed),
            new ExtractionInput(ExtractionInputType.Text, "clipboard", null, null, null, text, null, null),
            null);

    private static ExtractionPrincipalContext Principal(SeededPrincipal seed) =>
        new(seed.UserId, seed.TenantId, "personal", seed.ContextId);

    private async Task<string?> JobStatus(Guid tenantId, Guid jobId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select job_status from extraction_job where tenant_id = @t and extraction_job_id = @j;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("j", jobId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task<int> CountJobs(Guid tenantId, string idempotencyKey)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from extraction_job where tenant_id = @t and idempotency_key = @k;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("k", idempotencyKey);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountResults(Guid tenantId, Guid jobId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from extraction_result where tenant_id = @t and extraction_job_id = @j;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("j", jobId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountFields(Guid tenantId, Guid jobId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*) from extraction_field f
            join extraction_result r on r.extraction_result_id = f.extraction_result_id
            where r.tenant_id = @t and r.extraction_job_id = @j;
            """, connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("j", jobId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountAudit(Guid tenantId, Guid jobId, string eventType)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from extraction_audit_event where tenant_id = @t and extraction_job_id = @j and event_type = @e;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("j", jobId);
        command.Parameters.AddWithValue("e", eventType);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static NpgsqlConnectionFactory BuildFactory(string connectionString) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = connectionString })
            .Build());

    private sealed class FakeTranscriptionProvider : ITranscriptionProvider
    {
        public Task<TranscriptionOutput> TranscribeAsync(byte[] audio, string encoding, string? languageHint, CancellationToken cancellationToken) =>
            Task.FromResult(new TranscriptionOutput(
                "transcripción de prueba",
                [new TranscriptionSegment(0, "transcripción de prueba", 0, 1000, "es-MX", 0.9)],
                1000,
                new ModelInfo("Azure.OpenAI", "whisper", "v1")));
    }

    private sealed class FakeTextExtractionProvider : IStructuredTextExtractionProvider
    {
        public Task<StructuredExtractionOutput> ExtractAsync(string text, LanguageResolution language, CancellationToken cancellationToken) =>
            Task.FromResult(new StructuredExtractionOutput(
                "Resumen de prueba.",
                [
                    new ExtractedFact("summary", ExtractionFieldType.Text, "Resumen de prueba.", 0.9),
                    new ExtractedFact("action_item", ExtractionFieldType.Text, new { action = "Llamar a Ana" }, 0.88),
                    new ExtractedFact("entity", ExtractionFieldType.Entity, new { name = "Ana", entity_type = "person" }, 0.5)
                ],
                new ModelInfo("Azure.AI.Foundry", "model-router", "v1")));
    }
}
