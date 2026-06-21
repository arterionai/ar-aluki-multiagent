using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Security;
using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

/// <summary>
/// Transaction-scoped unit of work. The owning factory has already applied the
/// tenant/user session GUCs, so every repository operation is RLS-enforced.
/// </summary>
internal sealed class CaptureUnitOfWork : ICaptureUnitOfWork
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _completed;

    public CaptureUnitOfWork(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
        InboundEvents = new InboundEventRepository(connection, transaction);
        Messages = new MessageArtifactRepository(connection, transaction);
        Media = new MediaArtifactRepository(connection, transaction);
        Idempotency = new IdempotencyRepository(connection, transaction);
        Audit = new AuditEventRepository(connection, transaction);
    }

    public IInboundEventRepository InboundEvents { get; }

    public IMessageArtifactRepository Messages { get; }

    public IMediaArtifactRepository Media { get; }

    public IIdempotencyRepository Idempotency { get; }

    public IAuditEventRepository Audit { get; }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        await _transaction.RollbackAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch
            {
                // Best-effort rollback on dispose; connection teardown follows.
            }
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>Creates scoped <see cref="CaptureUnitOfWork"/> instances.</summary>
public sealed class CaptureUnitOfWorkFactory : ICaptureUnitOfWorkFactory
{
    private readonly NpgsqlConnectionFactory _connectionFactory;

    public CaptureUnitOfWorkFactory(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<ICaptureUnitOfWork> BeginAsync(PrincipalContext principal, CancellationToken cancellationToken) =>
        BeginScopedAsync(principal.TenantId, principal.UserId, cancellationToken);

    public Task<ICaptureUnitOfWork> BeginAuditScopeAsync(
        Guid tenantId,
        Guid? userId,
        CancellationToken cancellationToken) =>
        BeginScopedAsync(tenantId, userId, cancellationToken);

    private async Task<ICaptureUnitOfWork> BeginScopedAsync(
        Guid tenantId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.OpenAsync(cancellationToken);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ScopedSessionContextSetter.ApplyAsync(connection, transaction, tenantId, userId, cancellationToken);
            return new CaptureUnitOfWork(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            await connection.DisposeAsync();
            throw;
        }
    }
}
