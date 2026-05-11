import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react'
import * as signalR from '@microsoft/signalr'
import type { JobProgressDto, JobSummaryDto } from '../types'
import { api } from '../api/client'

// ── Types ─────────────────────────────────────────────────────────────────────

interface JobsContextValue {
  /** Live/last-known progress keyed by jobId */
  jobs: Map<number, JobProgressDto>
  /** Whether the SignalR connection is live */
  connected: boolean
  /** Create a test job and start it */
  createTestJob: (steps?: number) => Promise<JobSummaryDto>
  /** Request cancellation of a running job */
  cancelJob: (jobId: number) => Promise<void>
  /** Whether the jobs drawer is open */
  drawerOpen: boolean
  setDrawerOpen: (open: boolean) => void
}

const JobsContext = createContext<JobsContextValue | null>(null)

// ── Provider ──────────────────────────────────────────────────────────────────

export function JobsProvider({ children }: { children: React.ReactNode }) {
  const [jobs, setJobs]           = useState<Map<number, JobProgressDto>>(new Map())
  const [connected, setConnected] = useState(false)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const connectionRef = useRef<signalR.HubConnection | null>(null)

  // ── Bootstrap: load recent jobs from REST, then open SignalR ──────────────
  useEffect(() => {
    let cancelled = false

    // Pre-populate from REST so the drawer shows history even before any
    // live updates arrive.
    api.jobs.list().then(summaries => {
      if (cancelled) return
      setJobs(prev => {
        const next = new Map(prev)
        for (const s of summaries) {
          if (!next.has(s.id)) {
            next.set(s.id, summaryToProgress(s))
          }
        }
        return next
      })
    }).catch(() => { /* ignore — user may not be logged in yet */ })

    // Build and start SignalR connection
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/jobs')
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connection.on('JobProgressUpdate', (dto: JobProgressDto) => {
      setJobs(prev => new Map(prev).set(dto.jobId, dto))
    })

    connection.onreconnected(() => {
      if (cancelled) return
      setConnected(true)
      // Re-sync in case we missed messages while disconnected
      api.jobs.list().then(summaries => {
        if (cancelled) return
        setJobs(prev => {
          const next = new Map(prev)
          for (const s of summaries) next.set(s.id, summaryToProgress(s))
          return next
        })
      }).catch(() => {})
    })

    connection.onclose(() => { if (!cancelled) setConnected(false) })

    connection.start()
      .then(() => { if (!cancelled) setConnected(true) })
      .catch(() => { /* server may not be up yet; reconnect will retry */ })

    connectionRef.current = connection

    return () => {
      cancelled = true
      connection.stop()
      connectionRef.current = null
    }
  }, [])

  // ── Actions ────────────────────────────────────────────────────────────────

  const createTestJob = useCallback(async (steps?: number) => {
    const job = await api.jobs.createTest(steps)
    setJobs(prev => new Map(prev).set(job.id, summaryToProgress(job)))
    return job
  }, [])

  const cancelJob = useCallback(async (jobId: number) => {
    const updated = await api.jobs.cancel(jobId)
    setJobs(prev => new Map(prev).set(updated.id, summaryToProgress(updated)))
  }, [])

  return (
    <JobsContext.Provider value={{ jobs, connected, createTestJob, cancelJob, drawerOpen, setDrawerOpen }}>
      {children}
    </JobsContext.Provider>
  )
}

// ── Hook ──────────────────────────────────────────────────────────────────────

export function useJobs() {
  const ctx = useContext(JobsContext)
  if (!ctx) throw new Error('useJobs must be used inside <JobsProvider>')
  return ctx
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function summaryToProgress(s: JobSummaryDto): JobProgressDto {
  return {
    jobId:        s.id,
    jobType:      s.jobType,
    label:        s.label,
    status:       s.status,
    processed:    s.processedItems,
    total:        s.totalItems,
    successCount: s.processedItems - s.failedItems,
    failureCount: s.failedItems,
    currentItem:  s.currentItemLabel,
    startedAt:    s.startedAt,
    completedAt:  s.completedAt,
    errorMessage: s.errorMessage,
  }
}
