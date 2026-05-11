using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VRCGT.Infrastructure;

namespace VRCGT.Infrastructure.Tests;

/// <summary>
/// Keeps an in-memory SQLite connection alive for the lifetime of a test,
/// and creates the schema via EF Core EnsureCreated.
///
/// SQLite in-memory databases are destroyed when the last connection closes,
/// so we hold a persistent SqliteConnection here.
/// </summary>
internal sealed class SqliteTestDbFactory : IDbContextFactory<VrcgtDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<VrcgtDbContext> _options;

    public SqliteTestDbFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<VrcgtDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create the schema once; each CreateDbContext() reuses the same connection.
        using var ctx = new VrcgtDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public VrcgtDbContext CreateDbContext() => new(_options);

    public Task<VrcgtDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}
