import { useJobs } from '../context/JobsContext'
import type { JobProgressDto } from '../types'

const ACTIVE_STATUSES = new Set<string>(['Pending', 'Running', 'Cancelling'])

export default function JobsDrawer() {
  const { jobs, drawerOpen, setDrawerOpen, cancelJob } = useJobs()

  if (!drawerOpen) return null

  // Sort: active first, then recent completed
  const sorted = [...jobs.values()].sort((a, b) => {
    const aActive = ACTIVE_STATUSES.has(a.status) ? 0 : 1
    const bActive = ACTIVE_STATUSES.has(b.status) ? 0 : 1
    if (aActive !== bActive) return aActive - bActive
    // Within each group, most-recently started first
    return (b.startedAt ?? b.startedAt ?? '') > (a.startedAt ?? '') ? 1 : -1
  }).slice(0, 30)

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 z-30 bg-black/40"
        onClick={() => setDrawerOpen(false)}
      />

      {/* Drawer panel */}
      <aside className="fixed right-0 top-0 z-40 h-full w-96 bg-zinc-900 border-l border-zinc-700 flex flex-col shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-zinc-700 shrink-0">
          <span className="font-semibold text-sm text-zinc-100">Background Jobs</span>
          <button
            onClick={() => setDrawerOpen(false)}
            className="text-zinc-400 hover:text-zinc-100 text-lg leading-none"
            aria-label="Close jobs drawer"
          >
            ✕
          </button>
        </div>

        {/* Job list */}
        <div className="flex-1 overflow-y-auto p-3 space-y-2">
          {sorted.length === 0 && (
            <p className="text-zinc-500 text-sm text-center mt-8">No jobs yet.</p>
          )}
          {sorted.map(job => (
            <JobCard key={job.jobId} job={job} onCancel={cancelJob} />
          ))}
        </div>
      </aside>
    </>
  )
}

// ── Job card ──────────────────────────────────────────────────────────────────

function JobCard({
  job,
  onCancel,
}: {
  job: JobProgressDto
  onCancel: (id: number) => void
}) {
  const isActive    = ACTIVE_STATUSES.has(job.status)
  const pct         = job.total > 0 ? Math.round((job.processed / job.total) * 100) : 0
  const canCancel   = job.status === 'Running'

  return (
    <div className="rounded-lg bg-zinc-800 border border-zinc-700 p-3 space-y-2">
      {/* Title row */}
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium text-zinc-100 truncate">
            {job.label ?? job.jobType}
          </p>
          <p className="text-xs text-zinc-400 truncate">
            {job.currentItem ?? statusLabel(job.status)}
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <StatusBadge status={job.status} />
          {canCancel && (
            <button
              onClick={() => onCancel(job.jobId)}
              className="text-xs text-red-400 hover:text-red-300 px-1.5 py-0.5 rounded border border-red-800 hover:border-red-600"
              title="Cancel job"
            >
              Cancel
            </button>
          )}
        </div>
      </div>

      {/* Progress bar — only show while active or just completed */}
      {(isActive || job.status === 'Completed') && job.total > 0 && (
        <div>
          <div className="h-1.5 bg-zinc-700 rounded-full overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-300 ${
                job.status === 'Completed' ? 'bg-green-500' :
                job.status === 'Cancelling' ? 'bg-yellow-500' :
                'bg-violet-500'
              }`}
              style={{ width: `${pct}%` }}
            />
          </div>
          <p className="text-[10px] text-zinc-500 mt-0.5 text-right">
            {job.processed} / {job.total}
            {job.failureCount > 0 && ` · ${job.failureCount} failed`}
          </p>
        </div>
      )}

      {/* Error message */}
      {job.errorMessage && (
        <p className="text-xs text-red-400 truncate" title={job.errorMessage}>
          {job.errorMessage}
        </p>
      )}
    </div>
  )
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === 'Completed'  ? 'bg-green-900  text-green-300' :
    status === 'Failed'     ? 'bg-red-900    text-red-300'   :
    status === 'Cancelled'  ? 'bg-zinc-700   text-zinc-400'  :
    status === 'Cancelling' ? 'bg-yellow-900 text-yellow-300' :
    status === 'Running'    ? 'bg-violet-900 text-violet-300' :
    /* Pending */              'bg-zinc-700   text-zinc-400'
  return (
    <span className={`text-[10px] font-semibold px-1.5 py-0.5 rounded ${cls}`}>
      {status}
    </span>
  )
}

function statusLabel(status: string) {
  if (status === 'Pending')    return 'Waiting to start…'
  if (status === 'Running')    return 'Running…'
  if (status === 'Cancelling') return 'Cancelling…'
  return ''
}
