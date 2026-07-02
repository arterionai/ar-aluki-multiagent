namespace Aluki.Runtime.Memory.Chat;

/// <summary>
/// Single shared HttpClient for every Azure OpenAI / Foundry client in the worker.
/// The bounded PooledConnectionLifetime prevents stale TCP connections from the pool
/// causing TaskCanceledException on the first request after an idle period — the same
/// fix the chat router shipped with, now shared by embeddings, transcription and OCR.
/// </summary>
public static class AzureAiSharedHttp
{
    public static HttpClient Client { get; } = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });
}
