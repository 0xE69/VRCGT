import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { api } from '../api/client'
import { useGroups } from '../context/GroupContext'
import { useJobs } from '../context/JobsContext'
import DangerConfirmDialog from '../components/DangerConfirmDialog'
import type { BadgeScanDetailDto, BadgeScanMemberDto, BadgeScanSummaryDto } from '../types'

// ── Constants ─────────────────────────────────────────────────────────────────

const ROW_HEIGHT = 48  // px — must match row className height

type FilterMode = 'all' | 'verified' | 'unverified' | 'unknown'

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtTs(iso: string | null | undefined) {
  if (!iso) return '—'
  try { return new Date(iso).toLocaleString() }
  catch { return iso }
}

function statusChip(status: BadgeScanSummaryDto['status']) {
  const map: Record<string, string> = {
    Pending:   'bg-gray-700 text-gray-300',
    Running:   'bg-blue-900/60 text-blue-300 border border-blue-700',
    Completed: 'bg-green-900/60 text-green-300 border border-green-700',
    Failed:    'bg-red-900/60 text-red-300 border border-red-700',
    Cancelled: 'bg-yellow-900/60 text-yellow-300 border border-yellow-700',
  }
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${map[status] ?? 'bg-gray-700 text-gray-400'}`}>
      {status}
    </span>
  )
}

// ── No-group state ─────────────────────────────────────────────────────────────

function NoGroupSelected() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 py-24 text-center">
      <div className="text-4xl select-none">🔍</div>
      <div>
        <h2 className="text-lg font-semibold text-white">No group selected</h2>
        <p className="text-sm text-gray-400 mt-1">
          Select a group from the top bar to use the badge scanner.
        </p>
      </div>
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

export default function BadgeScannerPage() {
  const { selectedGroupId }         = useGroups()
  const { jobs }                    = useJobs()

  // ── Data state ───────────────────────────────────────────────────────────
  const [summary, setSummary]       = useState<BadgeScanSummaryDto | null>(null)
  const [detail,  setDetail]        = useState<BadgeScanDetailDto | null>(null)
  const [loading, setLoading]       = useState(false)
  const [, setScanning]              = useState(false)
  const [error,   setError]         = useState<string | null>(null)

  // ── Filter & selection state ─────────────────────────────────────────────
  const [filter, setFilter]         = useState<FilterMode>('all')
  const [selected, setSelected]     = useState<Set<string>>(new Set())

  // ── Confirmation dialog state ────────────────────────────────────────────
  type KickMode = 'selected' | 'unverified' | null
  const [kickMode, setKickMode]     = useState<KickMode>(null)

  // ── Live job progress tracking ───────────────────────────────────────────
  const activeScanJobId             = useRef<number | null>(null)
  const activeScanId                = useRef<number | null>(null)

  // ── Virtualizer ─────────────────────────────────────────────────────────
  const parentRef = useRef<HTMLDivElement>(null)

  // ── Derived data ─────────────────────────────────────────────────────────

  const filteredMembers = useMemo<BadgeScanMemberDto[]>(() => {
    if (!detail) return []
    switch (filter) {
      case 'verified':   return detail.members.filter(m => m.isAgeVerified === true)
      case 'unverified': return detail.members.filter(m => m.isAgeVerified === false)
      case 'unknown':    return detail.members.filter(m => m.isAgeVerified === null)
      default:           return detail.members
    }
  }, [detail, filter])

  const virtualizer = useVirtualizer({
    count:        filteredMembers.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan:     20,
  })

  // ── Active job progress ───────────────────────────────────────────────────

  const liveJob = activeScanJobId.current != null
    ? jobs.get(activeScanJobId.current)
    : null

  const scanIsRunning = liveJob != null
    && (liveJob.status === 'Running' || liveJob.status === 'Pending')

  // Watch job completion to auto-reload results
  useEffect(() => {
    if (!activeScanJobId.current || !activeScanId.current) return
    const j = jobs.get(activeScanJobId.current)
    if (!j) return
    if (j.status === 'Completed' || j.status === 'Failed' || j.status === 'Cancelled') {
      const sid = activeScanId.current
      const gid = selectedGroupId!
      activeScanJobId.current = null
      activeScanId.current    = null
      setScanning(false)
      // Reload full detail
      api.badgeScan.get(gid, sid)
        .then(d => { setDetail(d); setSummary(d) })
        .catch(() => {})
    }
  }, [jobs, selectedGroupId])

  // ── Initial load ──────────────────────────────────────────────────────────

  useEffect(() => {
    if (!selectedGroupId) {
      setSummary(null); setDetail(null); setSelected(new Set()); return
    }
    setLoading(true); setError(null)
    api.badgeScan.latest(selectedGroupId)
      .then(s => {
        setSummary(s)
        if (s && s.status === 'Completed') {
          return api.badgeScan.get(selectedGroupId, s.id).then(d => setDetail(d))
        }
      })
      .catch(err => setError(err?.message ?? 'Failed to load scan'))
      .finally(() => setLoading(false))
  }, [selectedGroupId])

  // ── Actions ───────────────────────────────────────────────────────────────

  const handleStartScan = useCallback(async () => {
    if (!selectedGroupId || scanIsRunning) return
    setError(null); setScanning(true)
    try {
      const s = await api.badgeScan.start(selectedGroupId)
      setSummary(s); setDetail(null); setSelected(new Set())
      activeScanJobId.current = s.jobId
      activeScanId.current    = s.id
    } catch (err: any) {
      setError(err?.message ?? 'Failed to start scan')
      setScanning(false)
    }
  }, [selectedGroupId, scanIsRunning])

  const handleKickConfirm = useCallback(async () => {
    if (!selectedGroupId || !detail) return
    const scanId = detail.id
    try {
      if (kickMode === 'selected') {
        const token = `KICK_SELECTED_BADGE:${scanId}`
        await api.badgeScan.kickSelected(selectedGroupId, scanId, [...selected], token)
      } else if (kickMode === 'unverified') {
        const token = `KICK_ALL_UNVERIFIED:${scanId}`
        await api.badgeScan.kickUnverified(selectedGroupId, scanId, token)
      }
    } catch (err: any) {
      setError(err?.message ?? 'Kick operation failed')
    } finally {
      setKickMode(null)
      setSelected(new Set())
    }
  }, [selectedGroupId, detail, kickMode, selected])

  const toggleRow = useCallback((userId: string) => {
    setSelected(prev => {
      const next = new Set(prev)
      if (next.has(userId)) next.delete(userId)
      else next.add(userId)
      return next
    })
  }, [])

  const selectAllUnverified = useCallback(() => {
    if (!detail) return
    setSelected(new Set(detail.members
      .filter(m => m.isAgeVerified === false && !m.wasKicked)
      .map(m => m.userId)))
  }, [detail])

  // ── Computed kick dialog content ──────────────────────────────────────────

  const kickCount =
    kickMode === 'selected'   ? selected.size :
    kickMode === 'unverified' ? (detail?.unverifiedCount ?? 0) :
    0

  const kickPhrase = `kick ${kickCount} members`

  // ── Render ────────────────────────────────────────────────────────────────

  if (!selectedGroupId) return <NoGroupSelected />

  return (
    <div className="flex flex-col gap-6 px-6 py-6 h-full">

      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-white">Badge Scanner</h1>
          <p className="text-sm text-gray-400 mt-0.5">
            Scan group members for the 18+ verification badge.
          </p>
        </div>

        <button
          onClick={handleStartScan}
          disabled={scanIsRunning || loading}
          className="px-4 py-2 rounded bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors"
        >
          {scanIsRunning ? 'Scan running…' : 'Start New Scan'}
        </button>
      </div>

      {/* Error banner */}
      {error && (
        <div className="rounded border border-red-700 bg-red-900/40 px-4 py-3 text-sm text-red-300">
          {error}
        </div>
      )}

      {/* Summary card */}
      {summary && (
        <div className="rounded-lg bg-gray-800 border border-gray-700 p-4 flex flex-wrap gap-6 text-sm">
          <div>
            <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Status</div>
            {statusChip(summary.status)}
          </div>
          <div>
            <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Scan started</div>
            <div className="text-gray-200">{fmtTs(summary.createdAt)}</div>
          </div>
          {summary.completedAt && (
            <div>
              <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Completed</div>
              <div className="text-gray-200">{fmtTs(summary.completedAt)}</div>
            </div>
          )}
          <div>
            <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Progress</div>
            <div className="text-gray-200">{summary.scannedMembers} / {summary.totalMembers || '?'}</div>
          </div>
          <div>
            <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Verified</div>
            <div className="text-green-400 font-medium">{summary.verifiedCount}</div>
          </div>
          <div>
            <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Unverified</div>
            <div className="text-red-400 font-medium">{summary.unverifiedCount}</div>
          </div>
          <div>
            <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Unknown</div>
            <div className="text-yellow-400 font-medium">{summary.unknownCount}</div>
          </div>
          {summary.kickedCount > 0 && (
            <div>
              <div className="text-gray-400 text-xs uppercase tracking-wide mb-1">Kicked</div>
              <div className="text-gray-200">{summary.kickedCount}</div>
            </div>
          )}
          {summary.errorMessage && (
            <div className="w-full text-red-400 text-xs mt-1">{summary.errorMessage}</div>
          )}
        </div>
      )}

      {/* Live progress bar */}
      {liveJob && scanIsRunning && (
        <div>
          <div className="flex justify-between text-xs text-gray-400 mb-1">
            <span>{liveJob.currentItem ?? 'Scanning…'}</span>
            <span>{liveJob.processed} / {liveJob.total || '?'}</span>
          </div>
          <div className="h-2 rounded bg-gray-700 overflow-hidden">
            <div
              className="h-full bg-indigo-500 transition-all duration-300"
              style={{ width: liveJob.total > 0 ? `${Math.round(liveJob.processed / liveJob.total * 100)}%` : '0%' }}
            />
          </div>
        </div>
      )}

      {/* Results section */}
      {detail && (
        <div className="flex flex-col gap-3 min-h-0 flex-1">

          {/* Filter + actions toolbar */}
          <div className="flex flex-wrap items-center justify-between gap-2">
            {/* Filter tabs */}
            <div className="flex gap-1">
              {(['all', 'verified', 'unverified', 'unknown'] as FilterMode[]).map(f => (
                <button
                  key={f}
                  onClick={() => setFilter(f)}
                  className={`px-3 py-1.5 rounded text-xs font-medium transition-colors ${
                    filter === f
                      ? 'bg-indigo-600 text-white'
                      : 'bg-gray-700 text-gray-300 hover:bg-gray-600'
                  }`}
                >
                  {f.charAt(0).toUpperCase() + f.slice(1)}
                  {f === 'all'        && ` (${detail.members.length})`}
                  {f === 'verified'   && ` (${detail.verifiedCount})`}
                  {f === 'unverified' && ` (${detail.unverifiedCount})`}
                  {f === 'unknown'    && ` (${detail.unknownCount})`}
                </button>
              ))}
            </div>

            {/* Action buttons */}
            <div className="flex gap-2 flex-wrap">
              <button
                onClick={selectAllUnverified}
                disabled={detail.unverifiedCount === 0}
                className="px-3 py-1.5 rounded text-xs bg-gray-700 hover:bg-gray-600 disabled:opacity-40 disabled:cursor-not-allowed text-gray-300 transition-colors"
              >
                Select All Unverified
              </button>
              {detail.unverifiedCount > 0 && (
                <button
                  onClick={() => setKickMode('unverified')}
                  className="px-3 py-1.5 rounded text-xs bg-red-800 hover:bg-red-700 text-white transition-colors"
                >
                  Kick All Unverified ({detail.unverifiedCount})
                </button>
              )}
              <a
                href={api.badgeScan.exportUrl(selectedGroupId, detail.id)}
                className="px-3 py-1.5 rounded text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 transition-colors"
              >
                Export CSV
              </a>
            </div>
          </div>

          {/* Bulk action bar */}
          {selected.size > 0 && (
            <div className="flex items-center gap-3 rounded border border-indigo-700 bg-indigo-900/30 px-4 py-2 text-sm">
              <span className="text-indigo-300 font-medium">{selected.size} selected</span>
              <button
                onClick={() => setKickMode('selected')}
                className="ml-auto px-3 py-1 rounded bg-red-700 hover:bg-red-600 text-white text-xs"
              >
                Kick Selected ({selected.size})
              </button>
              <button
                onClick={() => setSelected(new Set())}
                className="px-3 py-1 rounded bg-gray-700 hover:bg-gray-600 text-gray-300 text-xs"
              >
                Clear
              </button>
            </div>
          )}

          {/* Table header */}
          <div className="grid grid-cols-[auto_1fr_1fr_auto_auto] gap-2 px-3 py-2 rounded-t bg-gray-800 border border-gray-700 text-xs font-medium text-gray-400 uppercase tracking-wide">
            <div className="w-5" />
            <div>Display Name</div>
            <div>User ID</div>
            <div className="text-center">18+ Badge</div>
            <div className="text-center">Kicked</div>
          </div>

          {/* Virtualised table body */}
          <div
            ref={parentRef}
            className="flex-1 overflow-auto rounded-b border border-t-0 border-gray-700 bg-gray-850 min-h-0"
            style={{ height: 'calc(100vh - 500px)', minHeight: 240 }}
          >
            <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
              {virtualizer.getVirtualItems().map(vRow => {
                const m = filteredMembers[vRow.index]
                const isSelected = selected.has(m.userId)
                return (
                  <div
                    key={m.userId}
                    data-index={vRow.index}
                    ref={virtualizer.measureElement}
                    onClick={() => toggleRow(m.userId)}
                    style={{
                      position: 'absolute',
                      top: 0,
                      transform: `translateY(${vRow.start}px)`,
                      width: '100%',
                      height: ROW_HEIGHT,
                    }}
                    className={`grid grid-cols-[auto_1fr_1fr_auto_auto] gap-2 items-center px-3 cursor-pointer border-b border-gray-700 transition-colors ${
                      isSelected
                        ? 'bg-indigo-900/30'
                        : 'hover:bg-gray-700/50'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={() => {}}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-indigo-500 pointer-events-none"
                    />
                    <span className="truncate text-sm text-gray-200">{m.displayName}</span>
                    <span className="truncate text-xs text-gray-500 font-mono">{m.userId}</span>
                    <span className={`text-center text-xs font-medium ${
                      m.isAgeVerified === true  ? 'text-green-400' :
                      m.isAgeVerified === false ? 'text-red-400'   :
                      'text-yellow-400'
                    }`}>
                      {m.isAgeVerified === true ? '✓ Yes' : m.isAgeVerified === false ? '✗ No' : '?'}
                    </span>
                    <span className="text-center text-xs text-gray-500">
                      {m.wasKicked ? '✓' : ''}
                    </span>
                  </div>
                )
              })}
            </div>
          </div>

          <p className="text-xs text-gray-500 text-right">
            Showing {filteredMembers.length} of {detail.members.length} members
          </p>
        </div>
      )}

      {/* Empty state */}
      {!loading && !detail && !scanIsRunning && !summary && (
        <div className="flex flex-col items-center justify-center gap-3 py-20 text-center text-gray-400">
          <div className="text-3xl select-none">🔍</div>
          <p className="text-sm">No scan results yet. Press <strong>Start New Scan</strong> to begin.</p>
        </div>
      )}

      {/* Typed confirmation dialog */}
      <DangerConfirmDialog
        open={kickMode !== null}
        title={kickMode === 'selected' ? `Kick ${selected.size} selected members` : `Kick all ${detail?.unverifiedCount ?? 0} unverified members`}
        message={
          kickMode === 'selected'
            ? <span>This will kick <strong>{selected.size}</strong> selected members from the group. This cannot be undone.</span>
            : <span>This will kick all <strong>{detail?.unverifiedCount ?? 0}</strong> members without a verified 18+ badge. This cannot be undone.</span>
        }
        requiredPhrase={kickPhrase}
        confirmLabel={`Kick ${kickCount} members`}
        onConfirm={handleKickConfirm}
        onCancel={() => setKickMode(null)}
      />
    </div>
  )
}
