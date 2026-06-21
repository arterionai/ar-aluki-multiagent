using System.Collections.Concurrent;

namespace Aluki.Runtime.Functions.Channels.WhatsApp;

public sealed class InMemoryCaptureStore
{
    private readonly ConcurrentDictionary<string, Guid> _canonicalByKey = new(StringComparer.OrdinalIgnoreCase);

    public CaptureStoreResult Upsert(string idempotencyKey)
    {
        var canonicalMessageId = Guid.NewGuid();
        var existing = _canonicalByKey.GetOrAdd(idempotencyKey, canonicalMessageId);
        var isDuplicate = existing != canonicalMessageId;
        return new CaptureStoreResult(isDuplicate, existing);
    }
}

public readonly record struct CaptureStoreResult(bool IsDuplicate, Guid CanonicalMessageId);