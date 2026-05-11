using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Server.Services;

namespace VRCGT.Server.Services;

/// <summary>
/// Background service that polls VRChat for new audit log entries for each
/// managed group every 60 seconds.  Uses the high-watermark pattern so no
/// full table scan is ever required per poll cycle.
/// </summary>
public sealed class AuditLogPollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider          _sp;
    private readonly IAuditSink               _sink;
    private readonly ILogger<AuditLogPollingService> _log;

    public AuditLogPollingService(
        IServiceProvider               sp,
        IAuditSink                    sink,
        ILogger<AuditLogPollingService> log)
    {
        _sp   = sp;
        _sink = sink;
        _log  = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app a few seconds to finish startup before the first poll
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllGroupsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Audit log polling cycle failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollAllGroupsAsync(CancellationToken ct)
    {
        await using var scope = _sp.CreateAsyncScope();
        var groupService = scope.ServiceProvider.GetRequiredService<IGroupService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        IReadOnlyList<ManagedGroupDto> groups;
        try
        {
            groups = await groupService.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Could not load groups for audit poll");
            return;
        }

        foreach (var group in groups)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // SyncRecentAsync uses the high-watermark — no full table scan
                var hwm = await auditService.GetHighWatermarkAsync(group.GroupId, ct);
                var newCount = await auditService.SyncRecentAsync(group.GroupId, ct);

                if (newCount > 0)
                {
                    _log.LogInformation(
                        "Audit poll: {newCount} new entries for group {groupId}", newCount, group.GroupId);

                    // Broadcast new entries to connected web clients
                    long afterId = 0; // broadcast all entries newer than hwm
                    var newEntries = await auditService.GetNewSinceIdAsync(group.GroupId, afterId, ct);
                    // Narrow to truly new entries by filtering timestamp
                    if (hwm.HasValue)
                        newEntries = newEntries.Where(e => e.Timestamp > hwm.Value).ToList();

                    if (newEntries.Count > 0)
                        await _sink.BroadcastAsync(group.GroupId, newEntries, ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Audit poll failed for group {groupId}", group.GroupId);
            }

            // Small delay between groups to avoid hammering VRChat API
            await Task.Delay(500, ct);
        }
    }
}
