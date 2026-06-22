using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace Aluki.Runtime.Memory.Chat;

/// <summary>
/// Chat completion via the Azure AI Foundry model-router deployment (capability-
/// first, cost-optimized model selection). All inference stays on Azure.
/// </summary>
public interface IChatModelRouter
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
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
            return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        });
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var chat = _client.Value.GetChatClient(_deployment);
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var response = await chat.CompleteChatAsync(messages, new ChatCompletionOptions(), cancellationToken);
        var parts = response.Value.Content;
        return parts.Count > 0 ? parts[0].Text : string.Empty;
    }
}
