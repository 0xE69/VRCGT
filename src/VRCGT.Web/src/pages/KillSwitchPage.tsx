import { useCallback, useEffect, useRef, useState } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { api } from '../api/client'
import { useGroups } from '../context/GroupContext'
import { useJobs } from '../context/JobsContext'
import DangerConfirmDialog from '../components/DangerConfirmDialog'
import type {
  KillSwitchPreviewDto,
  KillSwitchSnapshotDto,
  GroupRoleDto,
} from '../types'

// ── Constants ─────────────────────────────────────────────────────────────────

const ROW_HEIGHT = 48

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtTs(iso: string | null | undefined) {
  if (!iso) return '—'
  try { return new Date(iso).toLocaleString() }
  catch { return iso }
}

function relativeAge(iso: string | null | undefined): string {
  if (!iso) return 'never'
  const diff = (Date.now() - new Date(iso).getTime()) / 60000
  if (diff < 1)   return 'just now'
  if (diff < 60)  return `${Math.round(diff)} min ago`
  return `${Math.round(diff / 60)} h ago`
}

// ── No-group state ─────────────────────────────────────────────────────────────

function NoGroupSelected() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 py-24 text-center">
      <div className="text-4xl select-none">⚡</div>
      <div>
        <h2 className="text-lg font-semibold text-white">No group selected</h2>
        <p className="text-sm text-gray-400 mt-1">
          Select a group from the top bar to use the Kill Switch.
        </p>
      </div>
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

export default function KillSwitchPage() {
  const { selectedGroupId } = useGroups()
  const { jobs }            = useJobs()

  // ── Preview state ────────────────────────────────────────────────────────
  const [preview,      setPreview]      = useState<KillSwitchPreviewDto | null>(null)
  const [loading,      setLoading]      = useState(false)
  const [refreshing,   setRefreshing]   = useState(false)
  const [error,        setError]        = useState<string | null>(null)

  // ── Role + member selection ───────────────────────────────────────────────
  const [selectedRoles, setSelectedRoles] = useState<Set<string>>(new Set())
  const [selectedUsers, setSelectedUsers] = useState<Set<string>>(new Set())

  // ── Snapshots ────────────────────────────────────────────────────────────
  const [snapshots,      setSnapshots]      = useState<KillSwitchSnapshotDto[]>([])
  const [snapsLoading,   setSnapsLoading]   = useState(false)

  // ── Confirmation dialogs ─────────────────────────────────────────────────
  type ConfirmMode = 'execute' | { restore: number } | null
  const [confirmMode,  setConfirmMode]  = useState<ConfirmMode>(null)

  // ── Live job progress ─────────────────────────────────────────────────────
  const activeJobId = useRef<number | null>(null)

  const liveJob = activeJobId.current != null ? jobs.get(activeJobId.current) : null
  const jobRunning = liveJob != null && (liveJob.status === 'Running' || liveJob.status === 'Pending')

  // Watch job completion → reload snapshots
  useEffect(() => {
    if (!activeJobId.current || !selectedGroupId) return
    const j = jobs.get(activeJobId.current)
    if (!j) return
    if (j.status === 'Completed' || j.status === 'Failed' || j.status === 'Cancelled') {
      activeJobId.current = null
      loadSnapshots(selectedGroupId)
    }
  }, [jobs, selectedGroupId])  // eslint-disable-line

  // ── Virtualizer (affected members table) ─────────────────────────────────
  const parentRef = useRef<HTMLDivElement>(null)

  const filteredMembers = (preview?.affectedMembers ?? []).filter(m => {
    if (selectedRoles.size === 0) return true
    return m.roleIds.some(r => selectedRoles.has(r))
  })

  const virtualizer = useVirtualizer({
    count:        filteredMembers.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan:     20,
  })

  // ── Load preview on group change ─────────────────────────────────────────

  useEffect(() => {
    if (!selectedGroupId) {
      setPreview(null); setSnapshots([]); setSelectedRoles(new Set()); setSelectedUsers(new Set())
      return
    }
    setLoading(true); setError(null)
    Promise.all([
      api.killSwitch.preview(selectedGroupId),
      loadSnapshots(selectedGroupId),
    ])
      .then(([p]) => setPreview(p))
      .catch(err => setError(err?.message ?? 'Failed to load kill switch data'))
      .finally(() => setLoading(false))
  }, [selectedGroupId]) // eslint-disable-line

  const loadSnapshots = useCallback(async (gid: string) => {
    setSnapsLoading(true)
    try { setSnapshots(await api.killSwitch.snapshots(gid)) }
    catch { /* ignore */ }
    finally { setSnapsLoading(false) }
  }, [])

  // ── Refresh ───────────────────────────────────────────────────────────────

  const handleRefresh = useCallback(async () => {
    if (!selectedGroupId || refreshing) return
    setRefreshing(true); setError(null)
    try {
      const p = await api.killSwitch.refresh(selectedGroupId)
      setPreview(p)
      setSelectedRoles(new Set()); setSelectedUsers(new Set())
    } catch (err: any) {
      setError(err?.message ?? 'Refresh failed')
    } finally {
      setRefreshing(false)
    }
  }, [selectedGroupId, refreshing])

  // ── Execute ───────────────────────────────────────────────────────────────

  const handleExecuteConfirm = useCallback(async () => {
    if (!selectedGroupId || selectedRoles.size === 0) return
    const token = `REMOVE_ROLES:${selectedGroupId}`
    try {
      const userIds = selectedUsers.size > 0 ? [...selectedUsers] : []
      const result  = await api.killSwitch.execute(
        selectedGroupId, [...selectedRoles], userIds, token)
      activeJobId.current = result.jobId
      setConfirmMode(null)
      setSelectedRoles(new Set()); setSelectedUsers(new Set())
      // Reload snapshots after a moment
      setTimeout(() => loadSnapshots(selectedGroupId), 1000)
    } catch (err: any) {
      setError(err?.message ?? 'Execute failed')
      setConfirmMode(null)
    }
  }, [selectedGroupId, selectedRoles, selectedUsers, loadSnapshots])

  // ── Restore ───────────────────────────────────────────────────────────────

  const handleRestoreConfirm = useCallback(async () => {
    if (confirmMode === null || confirmMode === 'execute') return
    const snapshotId = (confirmMode as { restore: number }).restore
    if (!selectedGroupId) return
    const token = `RESTORE_BACKUP:${snapshotId}`
    try {
      const { jobId } = await api.killSwitch.restore(selectedGroupId, snapshotId, token)
      activeJobId.current = jobId
      setConfirmMode(null)
    } catch (err: any) {
      setError(err?.message ?? 'Restore failed')
      setConfirmMode(null)
    }
  }, [selectedGroupId, confirmMode])

  // ── Derived ───────────────────────────────────────────────────────────────

  const toggleRole = (roleId: string) => {
    setSelectedRoles(prev => {
      const next = new Set(prev)
        if (next.has(roleId)) { next.delete(roleId) } else { next.add(roleId) }
      return next
    })
    setSelectedUsers(new Set()) // reset user filter when roles change
  }

  const toggleUser = (userId: string) => {
    setSelectedUsers(prev => {
      const next = new Set(prev)
      if (next.has(userId)) { next.delete(userId) } else { next.add(userId) }
      return next
    })
  }

  const affectedCount = selectedUsers.size > 0 ? selectedUsers.size : filteredMembers.length
  const roleRemovalCount = selectedUsers.size > 0
    ? filteredMembers
        .filter(m => selectedUsers.has(m.userId))
        .reduce((sum, m) => sum + m.roleIds.filter(r => selectedRoles.has(r)).length, 0)
    : filteredMembers.reduce((sum, m) => sum + m.roleIds.filter(r => selectedRoles.has(r)).length, 0)

  // ── Confirmation phrase helpers ───────────────────────────────────────────

  const executePhrase = 'REMOVE ROLES'
  const restorePhrase = 'RESTORE ROLES'

  // ── Render ────────────────────────────────────────────────────────────────

  if (!selectedGroupId) return <NoGroupSelected />

  return (
    <div className="flex flex-col gap-6 px-6 py-6">

      {/* ── Risk warning banner ── */}
      <div className="rounded-lg border border-red-700 bg-red-950/50 px-5 py-4">
        <div className="flex items-start gap-3">
          <span className="text-2xl mt-0.5 select-none flex-shrink-0">⚠️</span>
          <div>
            <h2 className="text-base font-bold text-red-300">Extreme caution — Kill Switch</h2>
            <p className="text-sm text-red-400 mt-1">
              This tool mass-removes VRChat group roles.
              A snapshot is created automatically before execution so you can restore,
              but role removal is immediate and affects real members.
              This cannot be undone without running a restore job.
            </p>
          </div>
        </div>
      </div>

      {/* Error banner */}
      {error && (
        <div className="rounded border border-red-700 bg-red-900/40 px-4 py-3 text-sm text-red-300">
          {error}
          <button onClick={() => setError(null)} className="ml-3 underline text-xs">Dismiss</button>
        </div>
      )}

      {/* ── Cache freshness gate ── */}
      {preview && (
        <div className={`rounded-lg border px-5 py-4 flex flex-wrap items-center gap-4 ${
          preview.isStale
            ? 'border-yellow-600 bg-yellow-950/40'
            : 'border-gray-700 bg-gray-800'
        }`}>
          <div className="flex-1 min-w-0">
            <div className="text-sm font-medium text-gray-200">
              Member cache
              {preview.isStale && <span className="ml-2 text-yellow-400 font-semibold">⚠ Stale</span>}
            </div>
            <div className="text-xs text-gray-400 mt-0.5">
              {preview.cacheTimestamp
                ? `Last refreshed ${relativeAge(preview.cacheTimestamp)} — ${fmtTs(preview.cacheTimestamp)}`
                : 'No cached data — refresh required before executing'}
            </div>
          </div>
          <div className="flex gap-4 text-sm text-gray-300 flex-wrap">
            <span><strong className="text-white">{preview.memberCount}</strong> members</span>
            <span><strong className="text-white">{preview.roleAssignmentCount}</strong> role assignments</span>
          </div>
          <button
            onClick={handleRefresh}
            disabled={refreshing || jobRunning}
            className="px-4 py-2 rounded bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors flex-shrink-0"
          >
            {refreshing ? 'Refreshing…' : preview.isStale ? 'Refresh Now (Required)' : 'Refresh Cache'}
          </button>
        </div>
      )}

      {/* Loading spinner */}
      {loading && (
        <div className="text-center text-gray-400 py-10">Loading kill switch data…</div>
      )}

      {/* ── Main execution panel (only shown when preview loaded and cache fresh) ── */}
      {preview && !loading && (
        <div className="flex flex-col gap-5">

          {/* Stale overlay note */}
          {preview.isStale && (
            <div className="rounded border border-yellow-700 bg-yellow-900/20 px-4 py-3 text-sm text-yellow-300">
              The member cache is older than 30 minutes.
              Press <strong>Refresh Now</strong> above before executing to ensure you're acting on current data.
            </div>
          )}

          <div className={`flex flex-col gap-5 ${preview.isStale ? 'pointer-events-none opacity-40' : ''}`}>

            {/* ── Step 1: Role selection ── */}
            <section className="rounded-lg border border-gray-700 bg-gray-800 p-5">
              <h3 className="text-sm font-semibold text-white mb-3 uppercase tracking-wide">
                Step 1 — Select roles to remove
              </h3>
              {preview.availableRoles.length === 0 ? (
                <p className="text-sm text-gray-400">No roles found for this group.</p>
              ) : (
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                  {preview.availableRoles.map((role: GroupRoleDto) => (
                    <label key={role.roleId} className="flex items-center gap-2 cursor-pointer group">
                      <input
                        type="checkbox"
                        checked={selectedRoles.has(role.roleId)}
                        onChange={() => toggleRole(role.roleId)}
                        className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-500"
                      />
                      <span className="text-sm text-gray-300 truncate group-hover:text-white transition-colors">
                        {role.name}
                      </span>
                    </label>
                  ))}
                </div>
              )}
            </section>

            {/* ── Step 2: Affected members table ── */}
            {selectedRoles.size > 0 && (
              <section className="rounded-lg border border-gray-700 bg-gray-800 p-5">
                <div className="flex items-center justify-between mb-3 flex-wrap gap-2">
                  <h3 className="text-sm font-semibold text-white uppercase tracking-wide">
                    Step 2 — Review affected members
                  </h3>
                  <div className="text-xs text-gray-400">
                    {filteredMembers.length} members · {selectedUsers.size > 0
                      ? `${selectedUsers.size} selected`
                      : 'all selected (deselect to exclude)'}
                  </div>
                </div>

                {/* Table header */}
                <div className="grid grid-cols-[auto_1fr_1fr] gap-2 px-3 py-2 rounded-t bg-gray-750 border border-gray-600 text-xs font-medium text-gray-400 uppercase tracking-wide">
                  <div className="w-5" />
                  <div>Display Name</div>
                  <div>Roles to Remove</div>
                </div>

                {/* Virtualised rows */}
                <div
                  ref={parentRef}
                  className="overflow-auto border border-t-0 border-gray-600 rounded-b"
                  style={{ height: Math.min(filteredMembers.length * ROW_HEIGHT + 4, 400), minHeight: 100 }}
                >
                  <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
                    {virtualizer.getVirtualItems().map(vRow => {
                      const m = filteredMembers[vRow.index]
                      const isSelected = selectedUsers.size === 0 || selectedUsers.has(m.userId)
                      const rolesToRemove = m.roleIds
                        .filter(r => selectedRoles.has(r))
                        .map((r) => m.roleNames[m.roleIds.indexOf(r)] ?? r)
                        .join(', ')

                      return (
                        <div
                          key={m.userId}
                          data-index={vRow.index}
                          ref={virtualizer.measureElement}
                          onClick={() => toggleUser(m.userId)}
                          style={{
                            position: 'absolute',
                            top: 0,
                            transform: `translateY(${vRow.start}px)`,
                            width: '100%',
                            height: ROW_HEIGHT,
                          }}
                          className={`grid grid-cols-[auto_1fr_1fr] gap-2 items-center px-3 cursor-pointer border-b border-gray-600 transition-colors text-sm ${
                            isSelected ? 'bg-red-950/20 hover:bg-red-950/30' : 'bg-gray-700/30 opacity-50 hover:opacity-70'
                          }`}
                        >
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => {}}
                            className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-500 pointer-events-none"
                          />
                          <span className="truncate text-gray-200">{m.displayName}</span>
                          <span className="truncate text-red-400 text-xs">{rolesToRemove}</span>
                        </div>
                      )
                    })}
                  </div>
                </div>

                <p className="text-xs text-gray-500 mt-1.5">
                  Click rows to exclude individual members. Unchecked members will be skipped.
                </p>
              </section>
            )}

            {/* ── Step 3: Execute button ── */}
            {selectedRoles.size > 0 && filteredMembers.length > 0 && (
              <section className="rounded-lg border border-red-800 bg-red-950/30 p-5 flex flex-wrap items-center gap-4">
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-semibold text-red-300">
                    Ready to execute
                  </div>
                  <div className="text-xs text-red-400 mt-0.5">
                    Will remove <strong>{roleRemovalCount}</strong> role assignment{roleRemovalCount !== 1 ? 's' : ''} from{' '}
                    <strong>{affectedCount}</strong> member{affectedCount !== 1 ? 's' : ''}.
                    A snapshot will be created first.
                  </div>
                </div>
                <button
                  onClick={() => setConfirmMode('execute')}
                  disabled={preview.isStale || jobRunning}
                  className="px-5 py-2.5 rounded bg-red-700 hover:bg-red-600 disabled:opacity-50 disabled:cursor-not-allowed text-sm font-bold text-white transition-colors flex-shrink-0"
                >
                  {jobRunning ? 'Job running…' : `Remove Roles (${affectedCount} members)`}
                </button>
              </section>
            )}
          </div>

          {/* ── Live job progress ── */}
          {liveJob && jobRunning && (
            <div className="rounded border border-blue-700 bg-blue-950/30 p-4">
              <div className="flex justify-between text-xs text-gray-400 mb-1">
                <span>{liveJob.currentItem ?? 'Running…'}</span>
                <span>{liveJob.processed} / {liveJob.total || '?'}</span>
              </div>
              <div className="h-2 rounded bg-gray-700 overflow-hidden">
                <div
                  className="h-full bg-red-600 transition-all duration-300"
                  style={{ width: liveJob.total > 0 ? `${Math.round(liveJob.processed / liveJob.total * 100)}%` : '4%' }}
                />
              </div>
              {liveJob.failureCount > 0 && (
                <p className="text-xs text-yellow-400 mt-1">{liveJob.failureCount} failures so far</p>
              )}
            </div>
          )}

          {/* ── Snapshots / Restore section ── */}
          <section className="rounded-lg border border-gray-700 bg-gray-800 p-5">
            <div className="flex items-center justify-between mb-3 flex-wrap gap-2">
              <h3 className="text-sm font-semibold text-white uppercase tracking-wide">
                Snapshots &amp; Restore
              </h3>
              <button
                onClick={() => loadSnapshots(selectedGroupId!)}
                disabled={snapsLoading}
                className="text-xs text-gray-400 hover:text-white disabled:opacity-50 transition-colors"
              >
                {snapsLoading ? 'Loading…' : 'Refresh'}
              </button>
            </div>

            {snapshots.length === 0 ? (
              <p className="text-sm text-gray-400">No snapshots yet. A snapshot will be created automatically when you execute the kill switch.</p>
            ) : (
              <div className="flex flex-col gap-2">
                {snapshots.map(snap => (
                  <div
                    key={snap.id}
                    className="flex flex-wrap items-center gap-4 rounded border border-gray-700 bg-gray-750 px-4 py-3 text-sm"
                  >
                    <div className="flex-1 min-w-0">
                      <div className="text-gray-200 font-medium">{fmtTs(snap.createdAt)}</div>
                      <div className="text-xs text-gray-400 mt-0.5">
                        {snap.memberCount} members · {snap.roleAssignmentCount} role assignments
                        {snap.restoredCount > 0 && (
                          <span className="ml-2 text-green-400">· {snap.restoredCount} restored {fmtTs(snap.restoredAt)}</span>
                        )}
                        {snap.label && <span className="ml-2 italic">{snap.label}</span>}
                      </div>
                    </div>
                    <button
                      onClick={() => setConfirmMode({ restore: snap.id })}
                      disabled={jobRunning}
                      className="px-3 py-1.5 rounded bg-green-800 hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed text-xs font-medium text-white transition-colors flex-shrink-0"
                    >
                      Restore
                    </button>
                  </div>
                ))}
              </div>
            )}
          </section>
        </div>
      )}

      {/* ── Execute confirmation dialog ── */}
      <DangerConfirmDialog
        open={confirmMode === 'execute'}
        title={`Remove roles from ${affectedCount} members`}
        message={
          <span>
            This will permanently remove <strong>{roleRemovalCount}</strong> role assignment{roleRemovalCount !== 1 ? 's' : ''} from{' '}
            <strong>{affectedCount}</strong> member{affectedCount !== 1 ? 's' : ''}.
            A snapshot will be created first so you can restore.
            This action is immediately visible in VRChat.
          </span>
        }
        requiredPhrase={executePhrase}
        confirmLabel="Remove Roles"
        onConfirm={handleExecuteConfirm}
        onCancel={() => setConfirmMode(null)}
      />

      {/* ── Restore confirmation dialog ── */}
      <DangerConfirmDialog
        open={confirmMode !== null && confirmMode !== 'execute'}
        title="Restore snapshot"
        message={
          <span>
            This will re-apply all role assignments from the selected snapshot.
            Members who were already removed from the group cannot be restored this way.
          </span>
        }
        requiredPhrase={restorePhrase}
        confirmLabel="Restore Roles"
        onConfirm={handleRestoreConfirm}
        onCancel={() => setConfirmMode(null)}
      />
    </div>
  )
}
