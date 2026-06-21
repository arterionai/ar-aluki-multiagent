using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.IntegrationTests;

internal sealed class FakeConsentStopPolicy : IConsentStopPolicy
{
    private readonly bool _stop;

    public FakeConsentStopPolicy(bool stop = false) => _stop = stop;

    public Task<bool> IsStopActiveAsync(
        PrincipalContext principal,
        string senderExternalId,
        CancellationToken cancellationToken) => Task.FromResult(_stop);
}

internal sealed class FakePrincipalContextResolver : IPrincipalContextResolver
{
    private readonly PrincipalResolution _resolution;

    public FakePrincipalContextResolver(PrincipalResolution resolution) => _resolution = resolution;

    public Task<PrincipalResolution> ResolveAsync(ChannelIdentity identity, CancellationToken cancellationToken)
    {
        // Carry the inbound correlation id into the principal so audits align.
        if (_resolution is { Success: true, Principal: { } principal })
        {
            return Task.FromResult(
                PrincipalResolution.Allow(principal with { CorrelationId = identity.CorrelationId }));
        }

        return Task.FromResult(_resolution);
    }
}

internal sealed class FakeInboundEventRepository : IInboundEventRepository
{
    public int InsertCount { get; private set; }

    public Task<Guid> InsertAsync(InboundMessageEventRow row, CancellationToken cancellationToken)
    {
        InsertCount++;
        return Task.FromResult(row.EventId);
    }
}

internal sealed class FakeMessageArtifactRepository : IMessageArtifactRepository
{
    public int InsertCount { get; private set; }

    public Task<Guid> InsertAsync(UnifiedMessageArtifactRow row, CancellationToken cancellationToken)
    {
        InsertCount++;
        return Task.FromResult(row.MessageId);
    }

    public Task<UnifiedMessageArtifactRow?> GetByIdAsync(Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult<UnifiedMessageArtifactRow?>(null);

    public Task<int> CountByProviderAsync(
        Guid tenantId, string sourceChannel, string providerMessageId, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

internal sealed class FakeMediaArtifactRepository : IMediaArtifactRepository
{
    public int InsertCount { get; private set; }

    public Task<Guid> InsertAsync(MediaArtifactRow row, CancellationToken cancellationToken)
    {
        InsertCount++;
        return Task.FromResult(row.MediaId);
    }

    public Task<int> CountByMessageAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult(0);
}

internal sealed class FakeIdempotencyRepository : IIdempotencyRepository
{
    private readonly bool _isNew;

    public FakeIdempotencyRepository(bool isNew) => _isNew = isNew;

    public Task<IdempotencyClaimResult> TryClaimAsync(
        Guid tenantId, string sourceChannel, string providerMessageId, CancellationToken cancellationToken) =>
        Task.FromResult(new IdempotencyClaimResult(
            _isNew,
            Guid.NewGuid(),
            _isNew ? null : Guid.NewGuid(),
            _isNew ? 0 : 1));

    public Task LinkCanonicalAsync(Guid idempotencyId, Guid canonicalMessageId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class FakeAuditEventRepository : IAuditEventRepository
{
    public List<CaptureAuditEventRow> Inserted { get; } = new();

    public Task<Guid> InsertAsync(CaptureAuditEventRow row, CancellationToken cancellationToken)
    {
        Inserted.Add(row);
        return Task.FromResult(row.AuditId);
    }
}

internal sealed class FakeCaptureUnitOfWork : ICaptureUnitOfWork
{
    public FakeCaptureUnitOfWork(bool isNew = true)
    {
        Idempotency = new FakeIdempotencyRepository(isNew);
    }

    public IInboundEventRepository InboundEvents { get; } = new FakeInboundEventRepository();
    public IMessageArtifactRepository Messages { get; } = new FakeMessageArtifactRepository();
    public IMediaArtifactRepository Media { get; } = new FakeMediaArtifactRepository();
    public IIdempotencyRepository Idempotency { get; }
    public IAuditEventRepository Audit { get; } = new FakeAuditEventRepository();

    public int CommitCount { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        CommitCount++;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Controllable factory: the persistence path (<see cref="BeginAsync"/>) fails
/// transiently for the first <c>failuresBeforeSuccess</c> calls; the audit path
/// (<see cref="BeginAuditScopeAsync"/>) always succeeds so denial/retry audits
/// remain observable.
/// </summary>
internal sealed class FakeCaptureUnitOfWorkFactory : ICaptureUnitOfWorkFactory
{
    private readonly int _failuresBeforeSuccess;
    private readonly bool _isNew;
    private int _beginCalls;

    public FakeCaptureUnitOfWorkFactory(int failuresBeforeSuccess = 0, bool isNew = true)
    {
        _failuresBeforeSuccess = failuresBeforeSuccess;
        _isNew = isNew;
    }

    public int PersistenceAttempts => _beginCalls;

    public List<FakeCaptureUnitOfWork> AuditUnits { get; } = new();

    public Task<ICaptureUnitOfWork> BeginAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        _beginCalls++;
        if (_beginCalls <= _failuresBeforeSuccess)
        {
            throw new TimeoutException($"Simulated transient failure #{_beginCalls}.");
        }

        return Task.FromResult<ICaptureUnitOfWork>(new FakeCaptureUnitOfWork(_isNew));
    }

    public Task<ICaptureUnitOfWork> BeginAuditScopeAsync(
        Guid tenantId, Guid? userId, CancellationToken cancellationToken)
    {
        var uow = new FakeCaptureUnitOfWork(_isNew);
        AuditUnits.Add(uow);
        return Task.FromResult<ICaptureUnitOfWork>(uow);
    }
}
