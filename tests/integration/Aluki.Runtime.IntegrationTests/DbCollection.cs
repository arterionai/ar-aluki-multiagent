using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Shares a single <see cref="DbCaptureFixture"/> across the database-backed test
/// classes so migrations are applied exactly once and the classes run serially
/// (avoiding concurrent <c>CREATE EXTENSION</c> races).
/// </summary>
[CollectionDefinition(Name)]
public sealed class DbCollection : ICollectionFixture<DbCaptureFixture>
{
    public const string Name = "Db";
}
