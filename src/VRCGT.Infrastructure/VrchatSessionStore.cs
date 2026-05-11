using System.Net;
using VRCGT.Core;

namespace VRCGT.Infrastructure;

/// <summary>
/// Singleton that owns the VRChat API <see cref="HttpClient"/> and its
/// <see cref="CookieContainer"/>.  Tracks login state for the single active
/// VRChat session and provides shared rate-limit throttling.
///
/// Cookies/tokens are NEVER surfaced through public properties.
/// Call <see cref="GetSession"/> to get a safe snapshot.
/// </summary>
public sealed class VrchatSessionStore : IDisposable
{
    // ── HTTP infrastructure ───────────────────────────────────────────────────

    internal readonly HttpClient      Http;
    internal readonly CookieContainer Cookies;

    // ── Session state ─────────────────────────────────────────────────────────

    private readonly object _stateLock = new();
    private bool    _isLoggedIn;
    private string? _userId;
    private string? _displayName;
    private string? _profilePicUrl;

    // ── Rate limiting ─────────────────────────────────────────────────────────

    private          DateTime       _lastRequest = DateTime.MinValue;
    private readonly SemaphoreSlim  _rateSem     = new(1, 1);
    internal  const  int            MinIntervalMs = 100;

    // ── Construction ──────────────────────────────────────────────────────────

    public VrchatSessionStore()
    {
        Cookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer          = Cookies,
            UseCookies               = true,
            AutomaticDecompression   = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        Http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.vrchat.cloud/api/1/"),
            // Generous timeout for paginated calls; individual calls are faster.
            Timeout     = TimeSpan.FromSeconds(30),
        };

        // VRChat requires a meaningful User-Agent to avoid 403s.
        Http.DefaultRequestHeaders.Add(
            "User-Agent", "VRCGT/2.0-alpha (https://github.com/0xE69/VRCGT)");
        Http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    // ── Session read/write ────────────────────────────────────────────────────

    public VrchatSessionDto GetSession()
    {
        lock (_stateLock)
            return new VrchatSessionDto(_isLoggedIn, _userId, _displayName, _profilePicUrl);
    }

    internal void SetSession(string userId, string displayName, string? profilePicUrl)
    {
        lock (_stateLock)
        {
            _isLoggedIn    = true;
            _userId        = userId;
            _displayName   = displayName;
            _profilePicUrl = profilePicUrl;
        }
    }

    internal void ClearSession()
    {
        lock (_stateLock)
        {
            _isLoggedIn    = false;
            _userId        = null;
            _displayName   = null;
            _profilePicUrl = null;
        }

        ExpireAllCookies();
    }

    /// <summary>
    /// Injects previously stored cookie values into the container for session restore.
    /// The raw values come from a secure store (encrypted DB field) — not from an API response.
    /// </summary>
    internal void InjectCookies(string authCookie, string? twoFactorCookie)
    {
        var baseUri = new Uri("https://api.vrchat.cloud");
        Cookies.Add(baseUri, new Cookie("auth",           authCookie,     "/", ".vrchat.cloud"));
        if (!string.IsNullOrEmpty(twoFactorCookie))
            Cookies.Add(baseUri, new Cookie("twoFactorAuth", twoFactorCookie, "/", ".vrchat.cloud"));
    }

    private void ExpireAllCookies()
    {
        try
        {
            var uri = new Uri("https://api.vrchat.cloud");
            foreach (Cookie c in Cookies.GetCookies(uri))
                c.Expired = true;
        }
        catch
        {
            // best-effort — if cookie container is in a bad state, swallow
        }
    }

    // ── Rate limiting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Enforces a minimum inter-request interval to stay within VRChat's rate limits.
    /// This semaphore is shared across all <see cref="VrchatApiClient"/> instances
    /// because this store is a singleton.
    /// </summary>
    internal async Task ThrottleAsync(CancellationToken ct)
    {
        await _rateSem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = (DateTime.UtcNow - _lastRequest).TotalMilliseconds;
            if (elapsed < MinIntervalMs)
                await Task.Delay((int)(MinIntervalMs - elapsed), ct).ConfigureAwait(false);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _rateSem.Release();
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Http.Dispose();
        _rateSem.Dispose();
    }
}
