using System.Threading.Channels;
using VRCGT.Core;

namespace VRCGT.Infrastructure;

/// <summary>
/// In-process job queue backed by an unbounded Channel{long}.
/// Registered as a singleton so the worker and endpoint share the same instance.
/// </summary>
public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<long> _ch = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(long jobId, CancellationToken ct = default)
        => _ch.Writer.WriteAsync(jobId, ct);

    public ValueTask<long> DequeueAsync(CancellationToken ct = default)
        => _ch.Reader.ReadAsync(ct);
}
