using VRCGT.Infrastructure;
using Xunit;

namespace VRCGT.Infrastructure.Tests;

/// <summary>
/// Tests for AppDataPathProvider — specifically the path resolution logic
/// and VRCGT_DATA_DIR override behaviour.
/// No real file I/O is performed except EnsureDirectoriesExist, which uses a temp dir.
/// </summary>
public sealed class AppDataPathProviderTests : IDisposable
{
    // Capture the env var state before each test so we can restore it afterwards.
    private readonly string? _previousDataDir =
        Environment.GetEnvironmentVariable("VRCGT_DATA_DIR");

    public void Dispose()
    {
        // Restore env var to the value it had before this test ran.
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", _previousDataDir);
    }

    // ── Default path (no env var) ──────────────────────────────────────────────

    [Fact]
    public void Default_paths_contain_VRCGT_and_v2()
    {
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", null);

        var provider = new AppDataPathProvider();

        Assert.Contains("VRCGT", provider.RootPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v2",    provider.RootPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_DatabasePath_ends_with_vrcgt_v2_db()
    {
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", null);

        var provider = new AppDataPathProvider();

        Assert.EndsWith("vrcgt-v2.db", provider.DatabasePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_subdirs_are_rooted_under_RootPath()
    {
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", null);

        var provider = new AppDataPathProvider();

        Assert.StartsWith(provider.RootPath, provider.LogsPath);
        Assert.StartsWith(provider.RootPath, provider.CachePath);
        Assert.StartsWith(provider.RootPath, provider.ExportsPath);
        Assert.StartsWith(provider.RootPath, provider.BackupsPath);
    }

    // ── VRCGT_DATA_DIR override ──────────────────────────────────────────────

    [Fact]
    public void Env_var_override_sets_RootPath_to_specified_directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"vrcgt-path-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", tempDir);

        var provider = new AppDataPathProvider();

        Assert.Equal(Path.GetFullPath(tempDir), provider.RootPath);
    }

    [Fact]
    public void Env_var_override_makes_DataPath_equal_to_RootPath()
    {
        // When the env var is set there is no sub "data/" folder — the directory
        // IS the data directory, to avoid {dir}/data/data nesting.
        var tempDir = Path.Combine(Path.GetTempPath(), $"vrcgt-path-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", tempDir);

        var provider = new AppDataPathProvider();

        Assert.Equal(provider.RootPath, provider.DataPath);
    }

    [Fact]
    public void Env_var_override_DatabasePath_is_inside_DataPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"vrcgt-path-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", tempDir);

        var provider = new AppDataPathProvider();

        Assert.StartsWith(provider.DataPath, provider.DatabasePath);
        Assert.EndsWith("vrcgt-v2.db", provider.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    // ── EnsureDirectoriesExist ────────────────────────────────────────────────

    [Fact]
    public void EnsureDirectoriesExist_creates_all_subdirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"vrcgt-ensure-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", tempDir);

        try
        {
            var provider = new AppDataPathProvider();
            provider.EnsureDirectoriesExist();

            Assert.True(Directory.Exists(provider.RootPath));
            Assert.True(Directory.Exists(provider.LogsPath));
            Assert.True(Directory.Exists(provider.CachePath));
            Assert.True(Directory.Exists(provider.ExportsPath));
            Assert.True(Directory.Exists(provider.BackupsPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
