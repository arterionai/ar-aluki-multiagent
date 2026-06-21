using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Abstractions.Persistence;

/// <summary>
/// Transactional boundary for capture persistence. The unit of work applies the
/// tenant/context/user session scope before exposing any repository so that
/// every read/write is RLS-enforced and fails closed without scope.
/// </summary>
public interface ICaptureUnitOfWork : IAsyncDisposable
{
    IInboundEventRepository InboundEvents { get; }

    IMessageArtifactRepository Messages { get; }

    IMediaArtifactRepository Media { get; }

    IIdempotencyRepository Idempotency { get; }

    IAuditEventRepository Audit { get; }

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Creates scoped capture units of work. Begins a transaction and applies the
/// PostgreSQL session GUCs (<c>app.current_tenant</c>, <c>app.current_user_id</c>)
/// required by row-level security.
/// </summary>
public interface ICaptureUnitOfWorkFactory
{
    /// <summary>Begins a fully scoped unit of work for an authorized principal.</summary>
    Task<ICaptureUnitOfWork> BeginAsync(PrincipalContext principal, CancellationToken cancellationToken);

    /// <summary>
    /// Begins a unit of work scoped to a tenant only, used to persist denial
    /// audit records when full principal/membership resolution failed. Reads of
    /// canonical artifacts remain blocked by RLS; only audit inserts are allowed.
    /// </summary>
    Task<ICaptureUnitOfWork> BeginAuditScopeAsync(
        Guid tenantId,
        Guid? userId,
        CancellationToken cancellationToken);
}
