using Aluki.Runtime.Abstractions.Memory;

namespace Aluki.Runtime.Capture.Memory;

/// <summary>
/// Default sink used when no personal-memory capability is registered. Capture
/// stays fully functional on its own; memory ingestion is purely additive.
/// </summary>
public sealed class NoOpMemoryIngestionSink : IMemoryIngestionSink
{
    public Task IngestAsync(MemoryIngestionItem item, CancellationToken cancellationToken) => Task.CompletedTask;
}
