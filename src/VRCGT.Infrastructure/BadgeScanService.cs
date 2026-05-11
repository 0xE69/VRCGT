using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Implements <see cref="IBadgeScanService"/>.
///
/// Rate-limiting strategy (mirrors v1):
///   • Pause 3 seconds every 100 user lookups.
///   • On 429, pause 30 seconds and retry once.
///   • 500ms delay between each kick.
/// </summary>
public sealed class BadgeScanService : IBadgeScanService
{
    // Pause every N individual GetUserAsync calls to respect VRChat rate limits
    private const int  BatchSize      = 100;
    private static readonly TimeSpan BatchDelay  = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KickDelay   = TimeSpan.FromMilliseconds(500);

    private readonly IDbContextFactory<VrcgtDbContext> _dbFactory;
    private readonly IVrchatApiClient                  _vrc;
    private readonly ILogger<BadgeScanService>         _log;

    public BadgeScanService(
        IDbContextFactory<VrcgtDbContext> dbFactory,
        IVrchatApiClient                  vrc,
        ILogger<BadgeScanService>         log)
    {
        _dbFactory = dbFactory;
        _vrc       = vrc;
        _log       = log;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<BadgeScanSummaryDto?> GetLatestAsync(
        string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var scan = await db.BadgeScans
            .Where(s => s.GroupId == groupId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return scan is null ? null : ToSummary(scan);
    }

    public async Task<BadgeScanDetailDto?> GetScanAsync(
        long scanId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var scan = await db.BadgeScans
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == scanId, ct);
        return scan is null ? null : ToDetail(scan);
    }

    // ── Scan creation ─────────────────────────────────────────────────────────

    public async Task<BadgeScanSummaryDto> StartScanAsync(
        string groupId, IBackgroundJobQueue queue, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var scan = new BadgeScan
        {
            GroupId   = groupId,
            Status    = BadgeScanStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        db.BadgeScans.Add(scan);
        await db.SaveChangesAsync(ct);

        // Create the background job that references the scan
        var job = new BackgroundJob
        {
            JobType        = "badge-scan",
            Label          = $"Badge scan — {groupId}",
            Status         = BackgroundJobStatus.Pending,
            CreatedAt      = DateTime.UtcNow,
            ParametersJson = System.Text.Json.JsonSerializer.Serialize(
                new { scanId = scan.Id }),
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);

        // Link scan → job
        scan.JobId = job.Id;
        await db.SaveChangesAsync(ct);

        await queue.EnqueueAsync(job.Id, ct);

        return ToSummary(scan);
    }

    // ── Scan job (the heavy lifting) ──────────────────────────────────────────

    public async Task RunScanJobAsync(
        long scanId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken ct = default)
    {
        // Mark running
        await UpdateScanAsync(scanId, s =>
        {
            s.Status = BadgeScanStatus.Running;
        }, ct);

        try
        {
            // 1. Read the groupId from the scan record
            string groupId;
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var scan = await db.BadgeScans.FindAsync(new object[] { scanId }, ct)
                    ?? throw new InvalidOperationException($"BadgeScan {scanId} not found");
                groupId = scan.GroupId;
            }

            // 2. Fetch all group members (paged by VRChat API)
            _log.LogInformation("[BadgeScan {id}] Fetching group members for {group}", scanId, groupId);
            await reportProgress(0, 0, "Fetching group members…", 0);

            var members = await _vrc.GetGroupMembersAsync(groupId, ct: ct);
            int total = members.Count;

            await UpdateScanAsync(scanId, s => s.TotalMembers = total, ct);
            await reportProgress(0, total, $"Fetched {total} members — scanning for 18+ badge…", 0);

            if (total == 0)
            {
                await FinishScanAsync(scanId, BadgeScanStatus.Completed, null, ct);
                return;
            }

            int scanned = 0, verified = 0, unverified = 0, unknown = 0, failed = 0;
            int batchCounter = 0;

            foreach (var member in members)
            {
                ct.ThrowIfCancellationRequested();

                // Rate-limit courtesy pause every BatchSize calls
                batchCounter++;
                if (batchCounter >= BatchSize)
                {
                    _log.LogDebug("[BadgeScan {id}] Batch pause ({n}/{t})", scanId, scanned, total);
                    await reportProgress(scanned, total,
                        $"Scanned {scanned}/{total} — pausing to respect rate limits…", failed);
                    await Task.Delay(BatchDelay, ct);
                    batchCounter = 0;
                }

                VrchatUserSummaryDto? user = null;
                try
                {
                    user = await _vrc.GetUserAsync(member.UserId, ct);
                }
                catch (VrchatRateLimitException)
                {
                    _log.LogWarning("[BadgeScan {id}] Rate limited — waiting 30s", scanId);
                    await reportProgress(scanned, total,
                        $"Rate limited — waiting 30 s… (scanned {scanned}/{total})", failed);
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    try { user = await _vrc.GetUserAsync(member.UserId, ct); }
                    catch { /* still failing — record as unknown */ }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "[BadgeScan {id}] GetUser failed for {userId}", scanId, member.UserId);
                }

                scanned++;

                bool? ageVerified = user?.IsAgeVerified;   // null if API call failed
                if (ageVerified == true)       verified++;
                else if (ageVerified == false) unverified++;
                else                           unknown++;

                // Persist this member's result
                await using (var db = await _dbFactory.CreateDbContextAsync(ct))
                {
                    // Upsert — in case a scan is resumed after crash
                    var existing = await db.BadgeScanMembers
                        .FirstOrDefaultAsync(m => m.ScanId == scanId && m.UserId == member.UserId, ct);

                    if (existing is null)
                    {
                        db.BadgeScanMembers.Add(new BadgeScanMember
                        {
                            ScanId        = scanId,
                            UserId        = member.UserId,
                            DisplayName   = user?.DisplayName ?? member.DisplayName,
                            IsAgeVerified = ageVerified,
                            ScannedAt     = DateTime.UtcNow,
                        });
                    }
                    else
                    {
                        existing.IsAgeVerified = ageVerified;
                        existing.DisplayName   = user?.DisplayName ?? member.DisplayName;
                        existing.ScannedAt     = DateTime.UtcNow;
                    }

                    await db.SaveChangesAsync(ct);
                }

                // Update scan counters every 5 members to reduce DB write pressure
                if (scanned % 5 == 0 || scanned == total)
                {
                    await UpdateScanAsync(scanId, s =>
                    {
                        s.ScannedMembers  = scanned;
                        s.VerifiedCount   = verified;
                        s.UnverifiedCount = unverified;
                        s.UnknownCount    = unknown;
                    }, ct);
                }

                await reportProgress(scanned, total,
                    $"Scanned {scanned}/{total} — {verified} verified · {unverified} unverified · {unknown} unknown",
                    failed);
            }

            // Final counter flush
            await UpdateScanAsync(scanId, s =>
            {
                s.ScannedMembers  = scanned;
                s.VerifiedCount   = verified;
                s.UnverifiedCount = unverified;
                s.UnknownCount    = unknown;
            }, ct);

            await FinishScanAsync(scanId, BadgeScanStatus.Completed, null, ct);
            _log.LogInformation(
                "[BadgeScan {id}] Complete: {v} verified, {u} unverified, {k} unknown out of {t}",
                scanId, verified, unverified, unknown, total);
        }
        catch (OperationCanceledException)
        {
            await FinishScanAsync(scanId, BadgeScanStatus.Cancelled, "Scan was cancelled.", ct: default);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[BadgeScan {id}] Scan failed", scanId);
            await FinishScanAsync(scanId, BadgeScanStatus.Failed, ex.Message, ct: default);
            throw;
        }
    }

    // ── Kick selected ─────────────────────────────────────────────────────────

    public async Task RunKickSelectedJobAsync(
        long scanId, IReadOnlyList<string> userIds,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken ct = default)
    {
        string groupId;
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var scan = await db.BadgeScans.FindAsync(new object[] { scanId }, ct)
                ?? throw new InvalidOperationException($"BadgeScan {scanId} not found");
            groupId = scan.GroupId;
        }

        int total = userIds.Count, kicked = 0, failed = 0;

        for (int i = 0; i < userIds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var userId = userIds[i];

            bool success = false;
            try   { success = await _vrc.KickGroupMemberAsync(groupId, userId, ct); }
            catch { }

            if (success)
            {
                kicked++;
                await UpdateMemberKickedAsync(scanId, userId, ct);
                await UpdateScanAsync(scanId, s => s.KickedCount++, ct);
            }
            else
            {
                failed++;
            }

            await reportProgress(i + 1, total,
                $"Kicking… {kicked} kicked, {failed} failed", failed);

            await Task.Delay(KickDelay, ct);
        }
    }

    // ── Kick unverified ───────────────────────────────────────────────────────

    public async Task RunKickUnverifiedJobAsync(
        long scanId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken ct = default)
    {
        List<string> unverified;
        string groupId;
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var scan = await db.BadgeScans
                .Include(s => s.Members)
                .FirstOrDefaultAsync(s => s.Id == scanId, ct)
                ?? throw new InvalidOperationException($"BadgeScan {scanId} not found");

            groupId = scan.GroupId;
            unverified = scan.Members
                .Where(m => m.IsAgeVerified == false && !m.WasKicked)
                .Select(m => m.UserId)
                .ToList();
        }

        await RunKickSelectedJobAsync(scanId, unverified, reportProgress, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task UpdateScanAsync(
        long scanId, Action<BadgeScan> mutate, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var scan = await db.BadgeScans.FindAsync(new object[] { scanId }, ct);
        if (scan is null) return;
        mutate(scan);
        await db.SaveChangesAsync(ct);
    }

    private async Task FinishScanAsync(
        long scanId, BadgeScanStatus status, string? error, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var scan = await db.BadgeScans.FindAsync(new object[] { scanId }, ct);
        if (scan is null) return;
        scan.Status      = status;
        scan.CompletedAt = DateTime.UtcNow;
        scan.ErrorMessage = error;
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateMemberKickedAsync(
        long scanId, string userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var member = await db.BadgeScanMembers
            .FirstOrDefaultAsync(m => m.ScanId == scanId && m.UserId == userId, ct);
        if (member is null) return;
        member.WasKicked = true;
        await db.SaveChangesAsync(ct);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static BadgeScanSummaryDto ToSummary(BadgeScan s) => new(
        s.Id, s.GroupId, (BadgeScanStatus)(int)s.Status,
        s.CreatedAt, s.CompletedAt,
        s.TotalMembers, s.ScannedMembers,
        s.VerifiedCount, s.UnverifiedCount, s.UnknownCount, s.KickedCount,
        s.JobId, s.ErrorMessage);

    private static BadgeScanDetailDto ToDetail(BadgeScan s) => new(
        s.Id, s.GroupId, (BadgeScanStatus)(int)s.Status,
        s.CreatedAt, s.CompletedAt,
        s.TotalMembers, s.ScannedMembers,
        s.VerifiedCount, s.UnverifiedCount, s.UnknownCount, s.KickedCount,
        s.JobId, s.ErrorMessage,
        s.Members.Select(m => new BadgeScanMemberDto(
            m.Id, m.UserId, m.DisplayName, m.IsAgeVerified, m.WasKicked, m.ScannedAt))
         .OrderBy(m => m.DisplayName)
         .ToList());
}
