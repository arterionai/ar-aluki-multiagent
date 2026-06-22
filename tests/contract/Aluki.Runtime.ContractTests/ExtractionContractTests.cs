using System.Text.Json;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Extraction;
using Aluki.Runtime.Extraction.Configuration;
using Aluki.Runtime.Extraction.Persistence;
using Aluki.Runtime.Extraction.Policies;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Extraction.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for the extraction-skill-v1 request/response shape and the
/// pre-persistence validation responses (400). These paths return before any
/// scope/DB access, so no PostgreSQL is required; the accepted/processing paths
/// are covered by the integration suite.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ExtractionContractTests
{
    private static ExtractionCoordinator BuildCoordinator()
    {
        // Unconfigured connection factory: the validation (400) paths never touch it.
        var config = new ConfigurationBuilder().Build();
        var factory = new NpgsqlConnectionFactory(config);
        return new ExtractionCoordinator(
            new ExtractionScopeGuard(factory),
            new ExtractionStore(factory),
            new ThrowingTranscriptionProvider(),
            new ThrowingTextExtractionProvider(),
            Options.Create(new ExtractionOptions()),
            NullLogger<ExtractionCoordinator>.Instance);
    }

    private static ExtractionPrincipalContext ValidPrincipal() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "personal", Guid.NewGuid());

    [Fact]
    public async Task Missing_principal_returns_400_invalid_payload()
    {
        var coordinator = BuildCoordinator();
        var request = new ExtractionRequest(
            ExtractionId: null, TenantId: null, CorrelationId: "c1",
            PrincipalContext: null,
            ExtractionInput: new ExtractionInput(ExtractionInputType.Text, null, null, null, null, "hi", null, null),
            ProcessingOptions: null);

        var result = await coordinator.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        var error = Assert.IsType<ExtractionErrorResponse>(result.Body);
        Assert.Equal(ExtractionErrorCode.InvalidPayload, error.Code);
        Assert.Equal("c1", error.CorrelationId);
    }

    [Fact]
    public async Task Missing_input_returns_400()
    {
        var coordinator = BuildCoordinator();
        var request = new ExtractionRequest(
            null, null, null, ValidPrincipal(), ExtractionInput: null, ProcessingOptions: null);

        var result = await coordinator.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.IsType<ExtractionErrorResponse>(result.Body);
    }

    [Fact]
    public async Task Text_input_without_text_returns_400()
    {
        var coordinator = BuildCoordinator();
        var request = new ExtractionRequest(
            null, null, null, ValidPrincipal(),
            new ExtractionInput(ExtractionInputType.Text, null, null, null, null, null, null, null),
            null);

        var result = await coordinator.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Unknown_input_type_returns_400()
    {
        var coordinator = BuildCoordinator();
        var request = new ExtractionRequest(
            null, null, null, ValidPrincipal(),
            new ExtractionInput("video", null, null, null, null, null, null, null),
            null);

        var result = await coordinator.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void Error_response_uses_snake_case_property_names()
    {
        var error = new ExtractionErrorResponse("abc", ExtractionErrorCode.ScopeDenied, "denied");
        var json = JsonSerializer.Serialize(error);

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("correlation_id", out _));
        Assert.True(document.RootElement.TryGetProperty("code", out _));
        Assert.True(document.RootElement.TryGetProperty("message", out _));
    }

    [Fact]
    public void Response_serializes_contract_fields_in_snake_case()
    {
        var response = new ExtractionResponse(
            Status: ExtractionResponseStatus.PartialSuccess,
            JobId: Guid.NewGuid(),
            JobStatus: ExtractionJobStatus.CompletedWithWarnings,
            CorrelationId: "c1",
            ExtractionResults: new ExtractionResults(
                ExtractionType.TextSummary, 0.82,
                [new ExtractionFieldDto("summary", ExtractionFieldType.Text, "s", 0.82, ConfidenceTier.Medium)],
                RawContent: null,
                ModelInfo: new ModelInfo("Azure.AI.Foundry", "model-router", "v1")),
            Warnings: [new WarningItem(ExtractionWarningCode.ConfidenceMedium, "m", ["summary"])],
            StatusUrl: "/api/extraction/jobs/x",
            ProcessingMetadata: new ProcessingMetadata(12, 1, 100.0));

        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("job_id", out _));
        Assert.True(root.TryGetProperty("job_status", out _));
        Assert.True(root.TryGetProperty("extraction_results", out var results));
        Assert.True(results.TryGetProperty("extraction_type", out _));
        Assert.True(results.TryGetProperty("overall_confidence", out _));
        Assert.True(results.TryGetProperty("extracted_fields", out var fields));
        Assert.True(fields[0].TryGetProperty("confidence_tier", out _));
        Assert.True(root.TryGetProperty("warnings", out var warnings));
        Assert.True(warnings[0].TryGetProperty("affected_fields", out _));
        Assert.True(root.TryGetProperty("status_url", out _));
    }

    private sealed class ThrowingTranscriptionProvider : ITranscriptionProvider
    {
        public Task<TranscriptionOutput> TranscribeAsync(byte[] audio, string encoding, string? languageHint, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("should not be called on validation paths");
    }

    private sealed class ThrowingTextExtractionProvider : IStructuredTextExtractionProvider
    {
        public Task<StructuredExtractionOutput> ExtractAsync(string text, LanguageResolution language, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("should not be called on validation paths");
    }
}
