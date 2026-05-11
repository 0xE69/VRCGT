import { useState } from 'react'
import { useGroups } from '../context/GroupContext'
import { useConfirm } from '../context/ConfirmContext'
import { ApiError } from '../api/client'
import type { GroupDetailDto } from '../types'

// ── helpers ───────────────────────────────────────────────────────────────────

function fmtDate(iso: string | null | undefined) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: 'medium', timeStyle: 'short',
  })
}

function fmtAge(seconds: number | null | undefined) {
  if (seconds == null) return null
  if (seconds < 60)   return `${Math.round(seconds)}s ago`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m ago`
  return `${Math.round(seconds / 3600)}h ago`
}

// ── sub-components ────────────────────────────────────────────────────────────

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center gap-5 py-24 text-center">
      <div className="w-16 h-16 rounded-2xl bg-gray-800 flex items-center justify-center text-3xl select-none">
        🏷️
      </div>
      <div>
        <h2 className="text-lg font-semibold text-white">No groups configured</h2>
        <p className="text-sm text-gray-400 mt-1 max-w-xs">
          Use the <span className="text-violet-400 font-medium">+ Add group</span> button
          in the top bar to add your first VRChat group.
        </p>
      </div>
      <p className="text-xs text-gray-600">
        You need to be logged into VRChat first — go to Settings → VRChat account.
      </p>
    </div>
  )
}

function StatChip({ label, value, accent }: { label: string; value: string | number; accent?: string }) {
  return (
    <div className={`flex flex-col items-center justify-center rounded-xl px-5 py-3 ${accent ?? 'bg-gray-800'}`}>
      <span className="text-xl font-bold text-white tabular-nums">{value}</span>
      <span className="text-xs text-gray-400 mt-0.5">{label}</span>
    </div>
  )
}

function GroupDashboard({ group, onRefresh, onRemove }: {
  group: GroupDetailDto
  onRefresh: (id: string) => Promise<GroupDetailDto>
  onRemove:  (id: string) => Promise<void>
}) {
  const confirm = useConfirm()
  const [refreshing, setRefreshing] = useState(false)
  const [refreshErr, setRefreshErr] = useState<string | null>(null)
  const [removing,   setRemoving]   = useState(false)
  const [removeErr,  setRemoveErr]  = useState<string | null>(null)

  const handleRefresh = async () => {
    setRefreshing(true)
    setRefreshErr(null)
    try {
      await onRefresh(group.groupId)
    } catch (e) {
      setRefreshErr(
        e instanceof ApiError ? e.message : 'Refresh failed.'
      )
    } finally {
      setRefreshing(false)
    }
  }

  const handleRemove = async () => {
    const ok = await confirm({
      title:          'Remove group',
      message:        (
        <span>
          This will stop managing <strong className="text-white">{group.name}</strong> and
          delete all locally cached member data for this group.
          VRChat group data is not affected.
        </span>
      ),
      severity:       'danger',
      confirmLabel:   'Remove group',
      requiredPhrase: `delete ${group.name}`,
    })
    if (!ok) return

    setRemoving(true)
    setRemoveErr(null)
    try {
      await onRemove(group.groupId)
    } catch (e) {
      setRemoveErr(e instanceof ApiError ? e.message : 'Failed to remove group.')
      setRemoving(false)
    }
  }

  const ageLabel = fmtAge(group.cacheAgeSeconds)

  return (
    <div className="flex flex-col gap-5">

      {/* Banner + header card */}
      <div className="rounded-xl overflow-hidden border border-gray-800 bg-gray-900">

        {/* Banner */}
        <div
          className="h-28 w-full bg-gradient-to-r from-violet-900/40 to-gray-800"
          style={group.bannerUrl ? {
            backgroundImage: `url(${group.bannerUrl})`,
            backgroundSize: 'cover',
            backgroundPosition: 'center',
          } : undefined}
        />

        {/* Name row */}
        <div className="px-5 pb-5 -mt-7 flex items-end gap-4">
          {/* Icon */}
          {group.iconUrl ? (
            <img
              src={group.iconUrl}
              alt={group.name}
              className="w-14 h-14 rounded-xl border-2 border-gray-900 object-cover shrink-0 bg-gray-800"
            />
          ) : (
            <div className="w-14 h-14 rounded-xl border-2 border-gray-900 bg-gray-800 flex items-center justify-center text-2xl shrink-0">
              🏷️
            </div>
          )}
          <div className="min-w-0 flex-1 pb-1">
            <h1 className="text-xl font-bold text-white truncate">{group.name}</h1>
            <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 mt-0.5">
              <span className="text-xs text-gray-500 font-mono">{group.groupId}</span>
              {group.shortCode && (
                <>
                  <span className="text-gray-700">·</span>
                  <span className="text-xs text-gray-400 font-medium">{group.shortCode}</span>
                </>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Stats row */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <StatChip label="Members"     value={group.memberCount.toLocaleString()} />
        <StatChip label="Online now"  value={group.onlineCount.toLocaleString()} accent="bg-green-900/30 border border-green-800/40" />
        <div className="col-span-2 flex flex-col justify-center rounded-xl px-5 py-3 bg-gray-800">
          <div className="flex items-center justify-between gap-2">
            <div>
              <span className="text-xs text-gray-500">Last refreshed</span>
              <p className="text-sm text-gray-200 font-medium mt-0.5">
                {fmtDate(group.lastRefreshedAt)}
                {ageLabel && (
                  <span className="ml-2 text-xs text-gray-500">({ageLabel})</span>
                )}
              </p>
              <p className="text-xs text-gray-600 mt-0.5">
                Source: <span className={group.source === 'live' ? 'text-green-400' : 'text-gray-400'}>
                  {group.source}
                </span>
              </p>
            </div>
            <button
              onClick={handleRefresh}
              disabled={refreshing}
              className="shrink-0 text-xs bg-gray-700 hover:bg-violet-600 disabled:opacity-40 text-gray-200 rounded-lg px-3 py-1.5 transition-colors"
            >
              {refreshing ? 'Refreshing…' : '↻ Refresh'}
            </button>
          </div>
          {refreshErr && (
            <p className="text-xs text-red-400 mt-2">{refreshErr}</p>
          )}
        </div>
      </div>

      {/* Description */}
      {group.description && (
        <section className="bg-gray-900 border border-gray-800 rounded-xl p-5">
          <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Description</h2>
          <p className="text-sm text-gray-300 whitespace-pre-line leading-relaxed">{group.description}</p>
        </section>
      )}

      {/* Meta */}
      <section className="bg-gray-900 border border-gray-800 rounded-xl p-5">
        <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-3">Details</h2>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
          {[
            ['Added',    fmtDate(group.addedAt)],
            ['Refreshed', fmtDate(group.lastRefreshedAt)],
          ].map(([label, value]) => (
            <div key={label} className="contents">
              <dt className="text-gray-500">{label}</dt>
              <dd className="text-gray-300 font-mono text-xs">{value}</dd>
            </div>
          ))}
        </dl>

        {/* Danger zone */}
        <div className="mt-5 pt-4 border-t border-gray-800 flex items-center justify-between gap-3">
          <div>
            <p className="text-xs font-medium text-red-400">Remove group</p>
            <p className="text-xs text-gray-500 mt-0.5">
              Stops managing this group and deletes all cached member data locally.
            </p>
          </div>
          <button
            onClick={handleRemove}
            disabled={removing}
            className="shrink-0 text-xs bg-red-900/40 hover:bg-red-700 disabled:opacity-40 text-red-300 hover:text-white border border-red-800/60 rounded-lg px-3 py-1.5 transition-colors"
          >
            {removing ? 'Removing…' : 'Remove group'}
          </button>
        </div>
        {removeErr && (
          <p className="text-xs text-red-400 mt-2">{removeErr}</p>
        )}
      </section>
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function DashboardPage() {
  const { groups, selectedGroupId, loading, error, refreshGroup, removeGroup } = useGroups()

  const activeGroup = groups.find(g => g.groupId === selectedGroupId) ?? null

  if (loading && groups.length === 0) {
    return (
      <div className="max-w-3xl mx-auto flex flex-col gap-5 animate-pulse">
        <div className="h-28 bg-gray-800 rounded-xl" />
        <div className="grid grid-cols-4 gap-3">
          {[1,2,3,4].map(i => <div key={i} className="h-16 bg-gray-800 rounded-xl" />)}
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="max-w-3xl mx-auto">
        <div className="text-sm text-red-400 bg-red-500/10 border border-red-500/20 rounded-xl px-4 py-3">
          {error}
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 overflow-y-auto">
      <div className="max-w-3xl mx-auto">
        {activeGroup
          ? <GroupDashboard group={activeGroup} onRefresh={refreshGroup} onRemove={removeGroup} />
          : <EmptyState />
        }
      </div>
    </div>
  )
}

