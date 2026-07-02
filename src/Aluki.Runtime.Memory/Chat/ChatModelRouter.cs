using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using System.ClientModel.Primitives;

namespace Aluki.Runtime.Memory.Chat;

/// <summary>
/// Optional per-call tuning for chat completions. Callers that parse JSON from the
/// completion must leave <see cref="MaxOutputTokens"/> null so output is never truncated
/// mid-document; conversational callers should cap it (WhatsApp replies are short) to
/// bound completion time. Temperature is nullable because the Foundry model-router can
/// route to reasoning-family models that reject the parameter.
/// </summary>
public sealed record ChatCallSettings(int? MaxOutputTokens = null, float? Temperature = null);

/// <summary>
/// Chat completion via the Azure AI Foundry model-router deployment (capability-
/// first, cost-optimized model selection). All inference stays on Azure.
/// </summary>
public interface IChatModelRouter
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);

    Task<string> CompleteAsync(string systemPrompt, string userPrompt, ChatCallSettings? settings, CancellationToken cancellationToken)
        => CompleteAsync(systemPrompt, userPrompt, cancellationToken);
}

public sealed class FoundryChatModelRouter : IChatModelRouter
{
    private readonly string _deployment;
    private readonly Lazy<AzureOpenAIClient> _client;

    public FoundryChatModelRouter(IConfiguration configuration)
    {
        _deployment = configuration["Foundry:ChatDeployment"] ?? "model-router";
        _client = new Lazy<AzureOpenAIClient>(() =>
        {
            var endpoint = configuration["Foundry:Endpoint"]
                ?? throw new InvalidOperationException("Foundry:Endpoint is not configured.");
            var apiKey = configuration["Foundry:ApiKey"]
                ?? throw new InvalidOperationException("Foundry:ApiKey is not configured.");
            var options = new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(AzureAiSharedHttp.Client),
                // Callers own the completion budget via their CTS (25-45 s); the SDK
                // default of ~3 exponential-backoff retries only produces zombie work
                // past the caller timeout, so allow a single retry at most.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 1),
                NetworkTimeout = TimeSpan.FromSeconds(30)
            };
            return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), options);
        });
    }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        => CompleteAsync(systemPrompt, userPrompt, settings: null, cancellationToken);

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        ChatCallSettings? settings,
        CancellationToken cancellationToken)
    {
        var chat = _client.Value.GetChatClient(_deployment);
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var response = await chat.CompleteChatAsync(messages, BuildOptions(settings), cancellationToken);
        var parts = response.Value.Content;
        return parts.Count > 0 ? parts[0].Text : string.Empty;
    }

    /// <summary>Maps optional per-call settings onto SDK options. Public for unit tests.</summary>
    public static ChatCompletionOptions BuildOptions(ChatCallSettings? settings)
    {
        var options = new ChatCompletionOptions();
        if (settings?.MaxOutputTokens is { } maxTokens)
        {
            options.MaxOutputTokenCount = maxTokens;
        }

        if (settings?.Temperature is { } temperature)
        {
            options.Temperature = temperature;
        }

        return options;
    }
}
