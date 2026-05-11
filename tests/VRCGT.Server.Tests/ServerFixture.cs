using Microsoft.AspNetCore.Builder;
using System.Net.Sockets;
using VRCGT.Server;
using Xunit;

namespace VRCGT.Server.Tests;

/// <summary>
/// Starts a real VRCGT server in-process on a free loopback port for the
/// duration of the test collection.  All storage is redirected to a unique
/// temporary directory via VRCGT_DATA_DIR; the directory is deleted on teardown.
///
/// Only one fixture instance is created per test collection, so the cost of
/// starting Kestrel and running EF Core migrations is paid once.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private string _tempDir = "";

    public HttpClient Client { get; private set; } = null!;
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vrcgt-server-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Point all VRCGT storage at the temp directory — no production files touched.
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", _tempDir);

        Port = GetFreePort();
        _app = await ServerAppFactory.CreateAsync(Port, []);

        // Start the server in the background (does not block).
        _ = _app.RunAsync();

        Client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{Port}")
        };

        // Poll /health until the server is accepting connections (or timeout after 15 s).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var resp = await Client.GetAsync("/health", cts.Token);
                if (resp.IsSuccessStatusCode) break;
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Client?.Dispose();

        // Restore the env var and clean up temp storage.
        Environment.SetEnvironmentVariable("VRCGT_DATA_DIR", null);

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// Collection marker so all server tests share a single <see cref="ServerFixture"/>
/// instance (one server startup, one teardown, no env-var conflicts between tests).
/// </summary>
[CollectionDefinition("Server")]
public sealed class ServerCollection : ICollectionFixture<ServerFixture> { }
