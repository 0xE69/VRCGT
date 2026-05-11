using System.Net;
using System.Text.Json;
using Xunit;

namespace VRCGT.Server.Tests;

/// <summary>
/// Integration tests for the built-in server endpoints:
///   GET /health
///   GET /api/v2/version
///   GET /api/system/storage
///
/// These tests start a real VRCGT server in-process on a loopback port.
/// No VRChat credentials or internet access are required.
/// </summary>
[Collection("Server")]
public sealed class ServerEndpointTests
{
    private readonly HttpClient _client;

    public ServerEndpointTests(ServerFixture fixture)
    {
        _client = fixture.Client;
    }

    // ── GET /health ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Health_endpoint_returns_200_OK()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_response_status_field_is_ok()
    {
        var response = await _client.GetAsync("/health");
        var json = await ParseJsonAsync(response);

        Assert.Equal("ok", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_response_mode_field_is_local()
    {
        var response = await _client.GetAsync("/health");
        var json = await ParseJsonAsync(response);

        Assert.Equal("local", json.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Health_response_contains_app_name()
    {
        var response = await _client.GetAsync("/health");
        var json = await ParseJsonAsync(response);

        var app = json.GetProperty("app").GetString();
        Assert.False(string.IsNullOrEmpty(app), "app field should not be empty");
    }

    // ── GET /api/v2/version ───────────────────────────────────────────────────

    [Fact]
    public async Task Version_endpoint_returns_200_OK()
    {
        var response = await _client.GetAsync("/api/v2/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Version_response_contains_app_field()
    {
        var response = await _client.GetAsync("/api/v2/version");
        var json = await ParseJsonAsync(response);

        Assert.True(json.TryGetProperty("app", out _), "Response should have 'app' field");
    }

    [Fact]
    public async Task Version_response_contains_version_field()
    {
        var response = await _client.GetAsync("/api/v2/version");
        var json = await ParseJsonAsync(response);

        Assert.True(json.TryGetProperty("version", out _), "Response should have 'version' field");
    }

    [Fact]
    public async Task Version_response_lanMode_is_false_by_default()
    {
        var response = await _client.GetAsync("/api/v2/version");
        var json = await ParseJsonAsync(response);

        Assert.True(json.TryGetProperty("lanMode", out var lanMode),
            "Response should have 'lanMode' field");
        Assert.False(lanMode.GetBoolean());
    }

    // ── GET /api/system/storage ───────────────────────────────────────────────

    [Fact]
    public async Task Storage_endpoint_returns_200_OK()
    {
        var response = await _client.GetAsync("/api/system/storage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Storage_response_contains_appDataPath()
    {
        var response = await _client.GetAsync("/api/system/storage");
        var json = await ParseJsonAsync(response);

        Assert.True(json.TryGetProperty("appDataPath", out _),
            "Response should have 'appDataPath' field");
    }

    [Fact]
    public async Task Storage_response_contains_databaseExists_field()
    {
        var response = await _client.GetAsync("/api/system/storage");
        var json = await ParseJsonAsync(response);

        Assert.True(json.TryGetProperty("databaseExists", out _),
            "Response should have 'databaseExists' field");
    }

    [Fact]
    public async Task Storage_databaseExists_is_true_after_server_starts()
    {
        // The server runs migrations on startup, so the database file must exist.
        var response = await _client.GetAsync("/api/system/storage");
        var json = await ParseJsonAsync(response);

        Assert.True(json.GetProperty("databaseExists").GetBoolean(),
            "Database should exist after server startup (migrations ran)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
