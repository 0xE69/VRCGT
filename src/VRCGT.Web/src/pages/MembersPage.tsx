import { useCallback, useEffect, useRef, useState } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { api, ApiError } from '../api/client'
import { useGroups } from '../context/GroupContext'
import { useConfirm } from '../context/ConfirmContext'
import type { GroupMemberDto, MemberPageDto } from '../types'
import MemberDrawer from '../components/MemberDrawer'

// ── Constants ─────────────────────────────────────────────────────────────────

const PAGE_SIZE  = 100   // rows per server page
const ROW_HEIGHT = 48    // px; must match the row className

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtDate(iso: string | null | undefined) {
  if (!iso) return '—'
  try { return new Date(iso).toLocaleDateString() }
  catch { return iso }
}

function fmtAge(seconds: number | null | undefined) {
  if (seconds == null) return null
  if (seconds < 60)   return `${Math.round(seconds)}s ago`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m ago`
  return `${Math.round(seconds / 3600)}h ago`
}

// ── No-group state ────────────────────────────────────────────────────────────

function NoGroupSelected() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 py-24 text-center">
      <div className="text-4xl select-none">👥</div>
      <div>
        <h2 className="text-lg font-semibold text-white">No group selected</h2>
        <p className="text-sm text-gray-400 mt-1">
          Select a group from the top bar to view its members.
        </p>
      </div>
    </div>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function MembersPage() {
  const { selectedGroupId } = useGroups()
  const confirm = useConfirm()

  // All loaded rows — we load pages on demand and accumulate
  const [rows,      setRows]      = useState<GroupMemberDto[]>([])
  const [total,     setTotal]     = useState(0)
  const [meta,      setMeta]      = useState<Pick<MemberPageDto,'source'|'lastRefreshedAt'|'cacheAgeSeconds'> | null>(null)
  const [loading,   setLoading]   = useState(false)
  const [loadErr,   setLoadErr]   = useState<string | null>(null)
  const [nextPage,  setNextPage]  = useState(0)
  const allLoaded = rows.length >= total && total > 0

  // Filters
  const [searchInput, setSearchInput] = useState('')
  const [search,      setSearch]      = useState('')   // debounced value
  const [roleFilter,  setRoleFilter]  = useState('')

  // Detail drawer
  const [selected, setSelected] = useState<GroupMemberDto | null>(null)

  // Refresh
  const [refreshing,  setRefreshing]  = useState(false)
  const [refreshMsg,  setRefreshMsg]  = useState<string | null>(null)

  // Debounce search input
  useEffect(() => {
    const t = setTimeout(() => setSearch(searchInput), 350)
    return () => clearTimeout(t)
  }, [searchInput])

  // Reset when group / filters change
  useEffect(() => {
    setRows([])
    setTotal(0)
    setMeta(null)
    setNextPage(0)
    setLoadErr(null)
  }, [selectedGroupId, search, roleFilter])

  // Fetch one page
  const fetchPage = useCallback(async (groupId: string, page: number) => {
    setLoading(true)
    try {
      const result = await api.members.list(groupId, {
        page,
        pageSize: PAGE_SIZE,
        search:   search || undefined,
        role:     roleFilter || undefined,
      })
      setRows(prev => page === 0 ? result.items : [...prev, ...result.items])
      setTotal(result.totalCount)
      setMeta({ source: result.source, lastRefreshedAt: result.lastRefreshedAt, cacheAgeSeconds: result.cacheAgeSeconds })
      setNextPage(page + 1)
      setLoadErr(null)
    } catch (e) {
      setLoadErr(e instanceof ApiError ? e.message : 'Failed to load members.')
    } finally {
      setLoading(false)
    }
  }, [search, roleFilter])

  // Trigger initial load
  useEffect(() => {
    if (selectedGroupId) fetchPage(selectedGroupId, 0)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedGroupId, search, roleFilter])

  // ── Virtualizer ──────────────────────────────────────────────────────────────
  const scrollRef = useRef<HTMLDivElement>(null)

  const virtualizer = useVirtualizer({
    count:           rows.length + (allLoaded ? 0 : 1), // +1 sentinel for infinite scroll
    getScrollElement: () => scrollRef.current,
    estimateSize:    () => ROW_HEIGHT,
    overscan:        10,
  })

  const virtualItems = virtualizer.getVirtualItems()

  // Infinite scroll: when the sentinel row is rendered, load next page
  useEffect(() => {
    const lastItem = virtualItems[virtualItems.length - 1]
    if (!lastItem) return
    if (lastItem.index >= rows.length && !loading && !allLoaded && selectedGroupId) {
      fetchPage(selectedGroupId, nextPage)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [virtualItems])

  // ── Refresh handler ───────────────────────────────────────────────────────────
  const handleRefresh = async () => {
    if (!selectedGroupId) return

    const memberCount = total > 0 ? total : undefined
    const ok = await confirm({
      title:        'Refresh member cache',
      message:      memberCount
        ? `This will pull all ${memberCount.toLocaleString()} members from the VRChat API and replace the local cache. This may take 10–30 seconds for large groups.`
        : 'This will pull all members from the VRChat API and replace the local cache. This may take 10–30 seconds for large groups.',
      severity:     'warning',
      confirmLabel: 'Refresh',
    })
    if (!ok) return

    setRefreshing(true)
    setRefreshMsg(null)
    try {
      const r = await api.members.refresh(selectedGroupId)
      setRefreshMsg(`Refreshed ${r.count.toLocaleString()} members`)
      // Reload from page 0
      setRows([])
      setTotal(0)
      setNextPage(0)
      await fetchPage(selectedGroupId, 0)
    } catch (e) {
      setRefreshMsg(e instanceof ApiError ? e.message : 'Refresh failed.')
    } finally {
      setRefreshing(false)
    }
  }

  // ── Early exits ───────────────────────────────────────────────────────────────
  if (!selectedGroupId) return <NoGroupSelected />

  // ── Render ────────────────────────────────────────────────────────────────────
  const ageLabel = fmtAge(meta?.cacheAgeSeconds)

  return (
    <div className="flex flex-col h-full gap-4">

      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-3 shrink-0">
        <div className="flex-1 min-w-48">
          <input
            type="search"
            value={searchInput}
            onChange={e => setSearchInput(e.target.value)}
            placeholder="Search name or user ID…"
            className="w-full text-sm bg-gray-800 border border-gray-700 text-gray-200 rounded-lg px-3 py-1.5 focus:outline-none focus:ring-1 focus:ring-violet-500 placeholder:text-gray-600"
          />
        </div>

        <input
          type="text"
          value={roleFilter}
          onChange={e => setRoleFilter(e.target.value)}
          placeholder="Filter by role ID…"
          className="text-sm bg-gray-800 border border-gray-700 text-gray-200 rounded-lg px-3 py-1.5 w-44 focus:outline-none focus:ring-1 focus:ring-violet-500 placeholder:text-gray-600"
        />

        <button
          onClick={handleRefresh}
          disabled={refreshing}
          className="text-xs bg-gray-700 hover:bg-violet-600 disabled:opacity-40 text-gray-200 rounded-lg px-3 py-1.5 transition-colors whitespace-nowrap"
        >
          {refreshing ? 'Refreshing…' : '↻ Refresh from VRChat'}
        </button>

        {/* Freshness */}
        <div className="flex items-center gap-2 text-xs text-gray-500 ml-auto">
          {meta && (
            <>
              <span className={`rounded-full px-2 py-0.5 border text-xs ${
                meta.source === 'live'
                  ? 'bg-green-500/10 text-green-400 border-green-500/20'
                  : 'bg-gray-700/50 text-gray-400 border-gray-600/40'
              }`}>
                {meta.source}
              </span>
              {ageLabel && <span>{ageLabel}</span>}
            </>
          )}
          {total > 0 && (
            <span>{rows.length.toLocaleString()} / {total.toLocaleString()}</span>
          )}
        </div>
      </div>

      {/* Status messages */}
      {refreshMsg && (
        <p className="text-xs text-green-400 shrink-0">{refreshMsg}</p>
      )}
      {loadErr && (
        <p className="text-xs text-red-400 bg-red-500/10 border border-red-500/20 rounded px-3 py-2 shrink-0">
          {loadErr}
        </p>
      )}

      {/* Table header */}
      <div className="grid grid-cols-[2fr_2fr_1fr_2fr_auto] gap-2 px-4 py-2 text-xs text-gray-500 uppercase tracking-wider border-b border-gray-800 shrink-0">
        <span>Display name</span>
        <span>User ID</span>
        <span>Joined</span>
        <span>Roles</span>
        <span className="w-16 text-right">Actions</span>
      </div>

      {/* Empty cache state */}
      {!loading && rows.length === 0 && !loadErr && (
        <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="text-3xl select-none">📭</div>
          <p className="text-sm text-gray-400">
            {search || roleFilter
              ? 'No members match the current filter.'
              : 'Member cache is empty — click "Refresh from VRChat" to populate it.'}
          </p>
        </div>
      )}

      {/* Virtualized rows */}
      {rows.length > 0 && (
        <div
          ref={scrollRef}
          className="flex-1 overflow-y-auto"
        >
          {/* Total height spacer so scrollbar reflects full list */}
          <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
            {virtualItems.map(vItem => {
              // Sentinel row
              if (vItem.index >= rows.length) {
                return (
                  <div
                    key="sentinel"
                    style={{ position: 'absolute', top: vItem.start, height: ROW_HEIGHT, width: '100%' }}
                    className="flex items-center justify-center text-xs text-gray-600"
                  >
                    {loading ? 'Loading more…' : allLoaded ? `All ${total.toLocaleString()} members loaded` : ''}
                  </div>
                )
              }

              const m = rows[vItem.index]
              return (
                <div
                  key={m.userId}
                  style={{ position: 'absolute', top: vItem.start, height: ROW_HEIGHT, width: '100%' }}
                  className={`grid grid-cols-[2fr_2fr_1fr_2fr_auto] gap-2 items-center px-4 border-b border-gray-800/60 hover:bg-gray-800/50 transition-colors ${
                    selected?.userId === m.userId ? 'bg-violet-900/20' : ''
                  }`}
                >
                  {/* Display name */}
                  <span className="text-sm text-gray-100 font-medium truncate flex items-center gap-1.5">
                    {m.isRepresenting && (
                      <span className="inline-block w-1.5 h-1.5 rounded-full bg-violet-400 shrink-0" title="Representing" />
                    )}
                    {m.displayName}
                  </span>

                  {/* User ID */}
                  <span className="text-xs text-gray-500 font-mono truncate">{m.userId}</span>

                  {/* Joined */}
                  <span className="text-xs text-gray-400">{fmtDate(m.joinedAt)}</span>

                  {/* Roles */}
                  <div className="flex flex-wrap gap-1 overflow-hidden max-h-8">
                    {m.roleNames.length > 0
                      ? m.roleNames.map((name, i) => (
                          <span
                            key={m.roleIds[i] ?? i}
                            className="text-xs bg-gray-700 text-gray-300 rounded-full px-1.5 py-0.5 truncate max-w-[6rem]"
                            title={name}
                          >
                            {name}
                          </span>
                        ))
                      : <span className="text-xs text-gray-600">—</span>
                    }
                  </div>

                  {/* Actions */}
                  <div className="w-16 flex justify-end">
                    <button
                      onClick={() => setSelected(m)}
                      className="text-xs text-gray-500 hover:text-violet-400 transition-colors px-2 py-1 rounded hover:bg-gray-800"
                    >
                      View
                    </button>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Loading spinner (initial load) */}
      {loading && rows.length === 0 && (
        <div className="flex items-center justify-center py-12 text-sm text-gray-500">
          <span className="animate-pulse">Loading members…</span>
        </div>
      )}

      {/* Member detail drawer */}
      <MemberDrawer member={selected} onClose={() => setSelected(null)} />
    </div>
  )
}
