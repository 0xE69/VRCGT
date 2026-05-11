// ============================================================
// VRCGT.Server — WebApplication factory
// ============================================================
// Centralises all ASP.NET Core host configuration so that the same
// WebApplication can be created and started by two callers:
//
//   1.  Program.cs (standalone):
//         dotnet run --project src/VRCGT.Server [--port <n>]
//
//   2.  VRCGT.Launcher (in-process hosting):
//         The Launcher picks an available port, calls Create(), starts the
//         WebApplication as a Task, polls /health, then opens the browser.
//         No child process is spawned — the server runs in the same process.
//
// Content-root strategy:
//   UseContentRoot is set explicitly to AppContext.BaseDirectory so that
//   wwwroot/ is resolved relative to the running executable regardless of
//   which project is the host:
//     - dotnet run --project VRCGT.Server  → AppContext.BaseDirectory = Server bin/
//       The web SDK copies wwwroot/ to the Server's bin/ on every build.
//     - dotnet run --project VRCGT.Launcher → AppContext.BaseDirectory = Launcher bin/
//       VRCGT.Launcher.csproj has a Content Include that copies wwwroot/ from
//       VRCGT.Server into the Launcher's bin/ at build time (PreserveNewest).
// ============================================================

using System.Net;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Infrastructure;
using VRCGT.Infrastructure.Entities;
using VRCGT.Server.Endpoints;
using VRCGT.Server.Hubs;
using VRCGT.Server.Middleware;
using VRCGT.Server.Services;

namespace VRCGT.Server;

/// <summary>
/// Builds and configures the VRCGT <see cref="WebApplication"/>.
/// </summary>
public static class ServerAppFactory
{
    /// <summary>
    /// Creates a configured, not-yet-started <see cref="WebApplication"/> bound
    /// to <c>127.0.0.1:<paramref name="port"/></c>.
    /// Runs storage initialisation (directory creation + EF Core migrations) before returning.
    /// Call <see cref="WebApplication.RunAsync()"/> to start Kestrel.
    /// </summary>
    /// <param name="port">TCP port Kestrel will listen on (loopback only).</param>
    /// <param name="args">Command-line arguments forwarded to the host builder.</param>
    public static async Task<WebApplication> CreateAsync(int port, string[] args)
    {
        // ---- Read LAN-mode sidecar BEFORE builder is created ----
        // The sidecar ({DataPath}/access-settings.json) is written by the
        // /api/system/access PATCH endpoint and read here each startup.
        // This must happen before UseKestrel so the bind address reflects the
        // saved setting.  No DI or EF Core is available at this point.
        var dataPath  = AppDataPathProvider.GetDefaultDataPath();
        var lanMode   = AccessSettingsHelper.IsLanModeEnabled(dataPath);
        var runtimeBindMode = lanMode ? BindMode.Lan : BindMode.LocalOnly;

        // ---- VRCGT_BIND_URL override (Docker / advanced deployments) ----
        // When VRCGT_BIND_URL is set, it takes priority over both the port
        // argument and the LAN-mode sidecar.  The URL must be a valid Kestrel
        // listen URL, e.g. http://0.0.0.0:45721.
        // The env var is the only supported way to expose the server on all
        // interfaces inside Docker; the normal LAN-mode opt-in UI is not shown
        // in headless/server deployments.
        var bindUrlEnv  = Environment.GetEnvironmentVariable("VRCGT_BIND_URL");
        bool useEnvUrl  = !string.IsNullOrWhiteSpace(bindUrlEnv);
        int  runtimePort = port;

        if (useEnvUrl)
        {
            if (Uri.TryCreate(bindUrlEnv, UriKind.Absolute, out var parsedUri))
            {
                runtimePort     = parsedUri.Port > 0 ? parsedUri.Port : port;
                runtimeBindMode = parsedUri.Host is "0.0.0.0" or "*" or "+"
                    ? BindMode.Lan
                    : BindMode.LocalOnly;
            }
            else
            {
                Console.Error.WriteLine(
                    $"[VRCGT] WARNING: VRCGT_BIND_URL '{bindUrlEnv}' is not a valid URL. " +
                    "Falling back to default bind address.");
                useEnvUrl = false;
            }
        }
        // Set ContentRootPath at construction time via WebApplicationOptions.
        // WebApplicationBuilder.WebHost.UseContentRoot() is not supported after
        // CreateBuilder() has already resolved the content root — it throws
        // NotSupportedException.  WebApplicationOptions is the correct approach.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args            = args,
            // Resolve wwwroot/ relative to the running executable (Server or Launcher).
            // See the file header for why this is needed in the Launcher scenario.
            ContentRootPath = AppContext.BaseDirectory
        });

        // ---- Kestrel ----
        // When VRCGT_BIND_URL is set (Docker), honour it directly — UseUrls
        // lets Kestrel parse the full URL including host and port.
        // Otherwise: default to loopback; add 0.0.0.0 when LAN mode is saved.
        if (useEnvUrl)
        {
            builder.WebHost.UseUrls(bindUrlEnv!);
        }
        else
        {
            builder.WebHost.UseKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port);
                if (lanMode)
                    options.Listen(IPAddress.Any, port);
            });
        }

        // ---- Logging ----
        // Suppress the ASP.NET Core hosting-lifetime messages ("Now listening on …",
        // "Application started") so the Launcher's own console header is not
        // interrupted.  Error-level messages from all other categories still appear.
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

        // ---- Services ----
        builder.Services.AddSingleton<IAppInfo, AppInfo>();

        // ---- Data directory ----
        // IAppDataPathProvider is a singleton; EnsureDirectoriesExist() is called
        // once after the host is built (see below) so the db directory exists before
        // EF Core opens the connection.
        builder.Services.AddSingleton<IAppDataPathProvider, AppDataPathProvider>();

        // ---- EF Core / SQLite ----
        // The database path is resolved through IAppDataPathProvider so it always
        // lands in the platform-appropriate local app data directory.
        // IDbContextFactory<VrcgtDbContext> is preferred over a scoped DbContext
        // because Minimal API handlers and background services may need to create
        // short-lived contexts independently.
        builder.Services.AddDbContextFactory<VrcgtDbContext>((sp, options) =>
        {
            var paths = sp.GetRequiredService<IAppDataPathProvider>();
            options.UseSqlite($"Data Source={paths.DatabasePath}");
        });

        // ---- VRChat API client ----
        // VrchatSessionStore is a singleton that owns the CookieContainer + HttpClient
        // so VRChat cookies persist for the lifetime of the server process.
        // VrchatApiClient is also singleton (delegates all state to the store).
        builder.Services.AddSingleton<VrchatSessionStore>();
        builder.Services.AddSingleton<IVrchatApiClient, VrchatApiClient>();

        // ---- Group management service ----
        builder.Services.AddScoped<IGroupService, GroupService>();

        // ---- Member cache service ----
        builder.Services.AddScoped<IMemberService, MemberService>();

        // ---- Background job system ----
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
        builder.Services.AddSingleton<JobHandlerRegistry>();
        builder.Services.AddSingleton<JobCancellationSource>();
        builder.Services.AddSingleton<IJobProgressSink, HubJobProgressSink>();
        builder.Services.AddHostedService<BackgroundJobWorker>();

        // ---- Audit log service ----
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddSingleton<IAuditSink, HubAuditSink>();
        builder.Services.AddHostedService<AuditLogPollingService>();

        // ---- Badge scan service ----
        builder.Services.AddScoped<IBadgeScanService, BadgeScanService>();

        // ---- Kill switch service ----
        builder.Services.AddScoped<IKillSwitchService, KillSwitchService>();

        // ---- Server access settings ----
        // RuntimeAccessInfo captures what Kestrel was actually started with.
        // AccessSettingsService reads/writes the sidecar JSON for persisting changes.
        builder.Services.AddSingleton(new RuntimeAccessInfo
        {
            Port     = runtimePort,
            BindMode = runtimeBindMode,
        });
        builder.Services.AddScoped<IAccessSettingsService, AccessSettingsService>();

        // ---- Local user auth ----
        builder.Services.AddScoped<IPasswordHasher<LocalUser>, PasswordHasher<LocalUser>>();
        builder.Services.AddScoped<ILocalUserService, LocalUserService>();

        // ---- Cookie authentication ----
        // HttpOnly, SameSite=Strict, no Secure flag (localhost is plain HTTP by design).
        // SameSite=Strict prevents the cookie from being sent by any cross-origin request,
        // which is the primary CSRF protection on a localhost-only server.
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name     = "vrcgt_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.None; // localhost is HTTP
                options.ExpireTimeSpan  = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                // Return 401 JSON instead of redirecting to a login page
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                    ctx.Response.ContentType = "application/json";
                    return ctx.Response.WriteAsJsonAsync(new { error = "unauthenticated" });
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode  = StatusCodes.Status403Forbidden;
                    ctx.Response.ContentType = "application/json";
                    return ctx.Response.WriteAsJsonAsync(new { error = "forbidden" });
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            foreach (var perm in Enum.GetValues<VrcgtPermission>())
            {
                options.AddPolicy($"perm:{perm}", p =>
                    p.RequireAuthenticatedUser()
                     .RequireClaim("vrcgt.perm", perm.ToString()));
            }
        });

        // Custom 403/401 JSON handler — must be registered before AddAuthorization
        // results are consumed by the middleware pipeline.
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler,
            PermissionDeniedHandler>();

        var app = builder.Build();

        // ---- Storage initialisation ----
        // Ensure data directories exist and apply any pending EF Core migrations.
        // EnsureCreated() is intentionally NOT used — migrations are the only safe
        // path because EnsureCreated() skips the Migrations table entirely.
        // In development, MigrateAsync() is a no-op when the database is current.
        var paths = app.Services.GetRequiredService<IAppDataPathProvider>();
        paths.EnsureDirectoriesExist();

        using (var scope = app.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<VrcgtDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            // MigrateAsync() creates the database if it does not exist and applies
            // all pending migrations.  Safe to call on every startup.
            await db.Database.MigrateAsync();
        }

        // ---- Static files ----
        // UseDefaultFiles must come before UseStaticFiles.
        // Middleware order: UseDefaultFiles → UseStaticFiles → Auth → FirstRunGate → API routes → SPA fallback
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        // Block non-setup API calls until the Owner account has been created
        app.UseMiddleware<FirstRunGateMiddleware>();

        // ================================================================
        // /health — unauthenticated; used by VRCGT.Launcher to detect
        // when the server is ready before opening the browser.
        // ================================================================
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            app = AppConstants.AppName,
            mode = "local"
        }));

        // ================================================================
        // /api/v2/version — returns runtime info displayed in the UI
        // footer and used as the baseline for update-check comparisons.
        // ================================================================
        app.MapGet("/api/v2/version", (IAppInfo appInfo, RuntimeAccessInfo runtimeAccess) => Results.Ok(new
        {
            app = appInfo.AppName,
            version = appInfo.Version,
            runtime = appInfo.RuntimeDescription,
            os = RuntimeInformation.OSDescription,
            arch = RuntimeInformation.OSArchitecture.ToString(),
            bindAddress = runtimeAccess.BindMode == BindMode.Lan ? "0.0.0.0" : AppConstants.DefaultBindAddress,
            port = port.ToString(),
            lanMode = runtimeAccess.BindMode == BindMode.Lan
        }));

        // ================================================================
        // /api/system/storage — local storage paths and database status.
        // Unauthenticated (local-only; not sensitive).
        // ================================================================
        app.MapGet("/api/system/storage", (IAppDataPathProvider pathProvider) =>
        {
            var dbExists = File.Exists(pathProvider.DatabasePath);
            return Results.Ok(new
            {
                appDataPath  = pathProvider.RootPath,
                dataPath     = pathProvider.DataPath,
                logsPath     = pathProvider.LogsPath,
                cachePath    = pathProvider.CachePath,
                exportsPath  = pathProvider.ExportsPath,
                backupsPath  = pathProvider.BackupsPath,
                databasePath = pathProvider.DatabasePath,
                databaseExists = dbExists
            });
        });

        app.MapHub<JobsHub>("/hubs/jobs");
        app.MapHub<AuditHub>("/hubs/audit");

        app.MapSetupEndpoints();
        app.MapAuthEndpoints();
        app.MapVrchatEndpoints();
        app.MapGroupEndpoints();
        app.MapMemberEndpoints();
        app.MapJobEndpoints();
        app.MapAuditLogEndpoints();
        app.MapBadgeScanEndpoints();
        app.MapKillSwitchEndpoints();
        app.MapLocalUserEndpoints();
        app.MapSystemAccessEndpoints();

        // ---- Register built-in job handlers ----
        var registry = app.Services.GetRequiredService<JobHandlerRegistry>();

        // badge-scan: runs the per-member age-verification scan for a group
        registry.Register("badge-scan", async ctx =>
        {
            long scanId;
            try
            {
                var p = System.Text.Json.JsonDocument.Parse(ctx.ParametersJson ?? "{}");
                scanId = p.RootElement.GetProperty("scanId").GetInt64();
            }
            catch
            {
                throw new InvalidOperationException("badge-scan: missing scanId parameter");
            }

            await using var scope = ctx.Services.CreateAsyncScope();
            var scanSvc = scope.ServiceProvider.GetRequiredService<IBadgeScanService>();
            await scanSvc.RunScanJobAsync(scanId, ctx.ReportProgressAsync, ctx.Token);
        });

        // badge-kick-selected: kick a list of user IDs from a scan
        registry.Register("badge-kick-selected", async ctx =>
        {
            long scanId;
            IReadOnlyList<string> userIds;
            try
            {
                var p = System.Text.Json.JsonDocument.Parse(ctx.ParametersJson ?? "{}");
                scanId  = p.RootElement.GetProperty("scanId").GetInt64();
                userIds = p.RootElement.GetProperty("userIds")
                           .EnumerateArray()
                           .Select(e => e.GetString() ?? string.Empty)
                           .ToList();
            }
            catch
            {
                throw new InvalidOperationException("badge-kick-selected: missing parameters");
            }

            await using var scope = ctx.Services.CreateAsyncScope();
            var scanSvc = scope.ServiceProvider.GetRequiredService<IBadgeScanService>();
            await scanSvc.RunKickSelectedJobAsync(scanId, userIds, ctx.ReportProgressAsync, ctx.Token);
        });

        // badge-kick-unverified: kick all unverified members from a scan
        registry.Register("badge-kick-unverified", async ctx =>
        {
            long scanId;
            try
            {
                var p = System.Text.Json.JsonDocument.Parse(ctx.ParametersJson ?? "{}");
                scanId = p.RootElement.GetProperty("scanId").GetInt64();
            }
            catch
            {
                throw new InvalidOperationException("badge-kick-unverified: missing scanId parameter");
            }

            await using var scope = ctx.Services.CreateAsyncScope();
            var scanSvc = scope.ServiceProvider.GetRequiredService<IBadgeScanService>();
            await scanSvc.RunKickUnverifiedJobAsync(scanId, ctx.ReportProgressAsync, ctx.Token);
        });

        // kill-switch-execute: removes selected roles from group members
        registry.Register("kill-switch-execute", async ctx =>
        {
            long snapshotId;
            try
            {
                var p = System.Text.Json.JsonDocument.Parse(ctx.ParametersJson ?? "{}");
                snapshotId = p.RootElement.GetProperty("snapshotId").GetInt64();
            }
            catch
            {
                throw new InvalidOperationException("kill-switch-execute: missing snapshotId parameter");
            }

            await using var scope = ctx.Services.CreateAsyncScope();
            var killSvc = scope.ServiceProvider.GetRequiredService<IKillSwitchService>();
            await killSvc.RunExecuteJobAsync(snapshotId, ctx.ReportProgressAsync, ctx.Token);
        });

        // kill-switch-restore: re-applies roles from a snapshot
        registry.Register("kill-switch-restore", async ctx =>
        {
            long snapshotId;
            try
            {
                var p = System.Text.Json.JsonDocument.Parse(ctx.ParametersJson ?? "{}");
                snapshotId = p.RootElement.GetProperty("snapshotId").GetInt64();
            }
            catch
            {
                throw new InvalidOperationException("kill-switch-restore: missing snapshotId parameter");
            }

            await using var scope = ctx.Services.CreateAsyncScope();
            var killSvc = scope.ServiceProvider.GetRequiredService<IKillSwitchService>();
            await killSvc.RunRestoreJobAsync(snapshotId, ctx.ReportProgressAsync, ctx.Token);
        });

        // fetch-audit-history: fetches all audit log pages from VRChat for a group
        registry.Register("fetch-audit-history", async ctx =>
        {
            string groupId;
            try
            {
                var parms = System.Text.Json.JsonDocument.Parse(ctx.ParametersJson ?? "{}");
                groupId = parms.RootElement.GetProperty("groupId").GetString() ?? "";
            }
            catch
            {
                throw new InvalidOperationException("fetch-audit-history: missing groupId parameter");
            }

            await using var scope = ctx.Services.CreateAsyncScope();
            var auditSvc = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await auditSvc.FetchHistoryAsync(groupId, ctx.ReportProgressAsync, ctx.Token);
        });

        // test-progress: walks through N steps with a 150ms delay each
        registry.Register("test-progress", async ctx =>
        {
            for (var i = 0; i < ctx.TotalItems; i++)
            {
                ctx.Token.ThrowIfCancellationRequested();
                await ctx.ReportProgressAsync(i + 1, ctx.TotalItems, $"Step {i + 1} of {ctx.TotalItems}", 0);
                await Task.Delay(150, ctx.Token);
            }
        });

        // ---- SPA fallback ----
        // Must be registered last.  Any path not matched by an API route above
        // returns index.html so the React client-side router can handle it.
        app.MapFallbackToFile("index.html");

        return app;
    }
}
