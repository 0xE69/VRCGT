using Microsoft.EntityFrameworkCore;
using VRCGT.Infrastructure;
using Xunit;

namespace VRCGT.Infrastructure.Tests;

/// <summary>
/// Verifies that a SQLite database file can be created in an arbitrary
/// temporary directory using EF Core EnsureCreated.
/// </summary>
public sealed class SqliteTempFolderTests
{
    [Fact]
    public async Task SQLite_database_can_be_created_and_queried_in_temp_folder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"vrcgt-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");

        try
        {
            // Pooling=False ensures the connection is fully released on Dispose
            // so the temp directory can be cleaned up on Windows.
            var options = new DbContextOptionsBuilder<VrcgtDbContext>()
                .UseSqlite($"Data Source={dbPath};Pooling=False")
                .Options;

            await using var ctx = new VrcgtDbContext(options);
            await ctx.Database.EnsureCreatedAsync();

            Assert.True(File.Exists(dbPath), "Database file should exist after EnsureCreated.");

            // Basic sanity: can query LocalUsers without error
            var count = await ctx.LocalUsers.CountAsync();
            Assert.Equal(0, count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
