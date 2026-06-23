using System.Globalization;
using System.Text;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;

namespace Aluki.Runtime.Memory.Embeddings;

/// <summary>Generates text embeddings for memory indexing and recall.</summary>
public interface IEmbeddingClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Azure OpenAI embedding client. Uses the <c>AiExtraction:*</c> configuration
/// (endpoint + key, Key Vault-backed) and the embeddings deployment
/// (default <c>gotnote-embeddings</c> = text-embedding-3-small, 1536 dims).
/// </summary>
public sealed class AzureOpenAIEmbeddingClient : IEmbeddingClient
{
    private readonly string _deployment;
    private readonly Lazy<AzureOpenAIClient> _client;

    public AzureOpenAIEmbeddingClient(IConfiguration configuration)
    {
        _deployment = configuration["AiExtraction:EmbeddingDeployment"] ?? "gotnote-embeddings";
        _client = new Lazy<AzureOpenAIClient>(() =>
        {
            var endpoint = configuration["AiExtraction:Endpoint"]
                ?? throw new InvalidOperationException("AiExtraction:Endpoint is not configured.");
            var apiKey = configuration["AiExtraction:ApiKey"]
                ?? throw new InvalidOperationException("AiExtraction:ApiKey is not configured.");
            return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        });
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var embeddingClient = _client.Value.GetEmbeddingClient(_deployment);
        var result = await embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return result.Value.ToFloats().ToArray();
    }

    /// <summary>Formats a vector as a pgvector text literal: <c>[v1,v2,...]</c>.</summary>
    public static string ToVectorLiteral(float[] embedding)
    {
        var sb = new StringBuilder(embedding.Length * 8 + 2);
        sb.Append('[');
        for (var i = 0; i < embedding.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(embedding[i].ToString("R", CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }
}
