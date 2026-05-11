import { useCallback, useEffect, useRef, useState } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import * as signalR from '@microsoft/signalr'
import { api, ApiError } from '../api/client'
import { useGroups } from '../context/GroupContext'
import type { AuditLogEntryDto, AuditLogPageDto } from '../types'

// ── Constants ─────────────────────────────────────────────────────────────────

const PAGE_SIZE  = 100
const ROW_HEIGHT = 52   // px — must match row className

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtTs(iso: string | null | undefined) {
  if (!iso) return '—'
  try { return new Date(iso).toLocaleString() }
  catch { return iso }
}

function actionLabel(action: string): string {
  return action
    .replace(/^group\./, '')
    .replace(/\./g, ' › ')
}

function sourceBadge(source: 'vrchat' | 'vrcgt') {
  return source === 'vrcgt'
    ? <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium bg-indigo-900/60 text-indigo-300 border border-indigo-700">VRCGT</span>
    : <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium bg-gray-800 text-gray-400 border border-gray-700">VRChat</span>
}

// ── No-group state ─────────────────────────────────────────────────────────────

function NoGroupSelected() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 py-24 text-center">
      <div className="text-4xl select-none">📋</div>
      <div>
        <h2 className="text-lg font-semibold text-white">No group selected</h2>
        <p className="text-sm text-gray-400 mt-1">
          Select a group from the top bar to view its audit log.
        </p>
      </div>
    </div>
  )
}

// ── Main page ──────────────────────────────────────────────────────────────────

export default function AuditLogPage() {
  const { selectedGroupId } = useGroups()

  // Rows accumulate as pages are loaded
  const [rows,     setRows]     = useState<AuditLogEntryDto[]>([])
  const [total,    setTotal]    = useState(0)
  const [nextPage, setNextPage] = useState(0)
  const [loading,  setLoading]  = useState(false)
  const [loadErr,  setLoadErr]  = useState<string | null>(null)
  const allLoaded = rows.length >= total && total > 0

  // Latest row id — used for live update dedup
  const latestIdRef = useRef<number>(0)

  // Filters
  const [searchInput, setSearchInput] = useState('')
  const [search,      setSearch]      = useState('')
  const [typeFilter,  setTypeFilter]  = useState('')
  const [fromDate,    setFromDate]    = useState('')
  const [toDate,      setToDate]      = useState('')

  // Live status
  const [connected, setConnected] = useState(false)
  const [liveCount, setLiveCount] = useState(0)
  const hubRef = useRef<signalR.HubConnection | null>(null)

  // Fetch history job
  const [fetching,    setFetching]    = useState(false)
  const [fetchJobId,  setFetchJobId]  = useState<number | null>(null)
  const [fetchMsg,    setFetchMsg]    = useState<string | null>(null)

  // Debounce search input
  useEffect(() => {
    const t = setTimeout(() => setSearch(searchInput), 350)
    return () => clearTimeout(t)
  }, [searchInput])

  // Reset on group / filter change
  useEffect(() => {
    setRows([])
    setTotal(0)
    setNextPage(0)
    setLoadErr(null)
    latestIdRef.current = 0
  }, [selectedGroupId, search, typeFilter, fromDate, toDate])

  // Load a page of results
  const loadPage = useCallback(async (page: number) => {
    if (!selectedGroupId || loading) return
    setLoading(true)
    setLoadErr(null)
    try {
      const result: AuditLogPageDto = await api.auditLog.list(selectedGroupId, {
        page,
        pageSize: PAGE_SIZE,
        search:   search || undefined,
        type:     typeFilter || undefined,
        from:     fromDate   || undefined,
        to:       toDate     || undefined,
      })
      setTotal(result.totalCount)
      setRows(prev => page === 0 ? result.items : [...prev, ...result.items])
      setNextPage(page + 1)
      if (result.latestId != null && result.latestId > latestIdRef.current)
        latestIdRef.current = result.latestId
    } catch (err) {
      setLoadErr(err instanceof ApiError ? err.message : 'Failed to load audit log')
    } finally {
      setLoading(false)
    }
  }, [selectedGroupId, loading, search, typeFilter, fromDate, toDate])

  // Initial load
  useEffect(() => {
    if (selectedGroupId) loadPage(0)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedGroupId, search, typeFilter, fromDate, toDate])

  // ── SignalR live updates ───────────────────────────────────────────────────

  useEffect(() => {
    if (!selectedGroupId) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/audit')
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    hubRef.current = connection

    connection.on('NewAuditLogEntries', (entries: AuditLogEntryDto[]) => {
      const newEntries = entries.filter(e => e.id > latestIdRef.current)
      if (newEntries.length === 0) return
      const maxId = Math.max(...newEntries.map(e => e.id))
      if (maxId > latestIdRef.current) latestIdRef.current = maxId
      // Prepend newest entries (they arrive newest-last from server)
      setRows(prev => [...newEntries.reverse(), ...prev])
      setTotal(prev => prev + newEntries.length)
      setLiveCount(prev => prev + newEntries.length)
    })

    connection.onreconnected(() => {
      setConnected(true)
      connection.invoke('SubscribeToGroup', selectedGroupId).catch(() => {})
    })

    connection.onclose(() => setConnected(false))

    connection.start()
      .then(() => {
        setConnected(true)
        return connection.invoke('SubscribeToGroup', selectedGroupId)
      })
      .catch(() => setConnected(false))

    return () => {
      connection.invoke('UnsubscribeFromGroup', selectedGroupId).catch(() => {})
      connection.stop()
      setConnected(false)
      hubRef.current = null
    }
  }, [selectedGroupId])

  // ── Virtualizer ───────────────────────────────────────────────────────────

  const parentRef = useRef<HTMLDivElement>(null)
  const virtualizer = useVirtualizer({
    count:           rows.length,
    getScrollElement: () => parentRef.current,
    estimateSize:    () => ROW_HEIGHT,
    overscan:        20,
  })

  const virtualItems  = virtualizer.getVirtualItems()
  const totalListSize = virtualizer.getTotalSize()

  // Load more when scrolled near bottom
  useEffect(() => {
    const lastItem = virtualItems[virtualItems.length - 1]
    if (!lastItem) return
    if (lastItem.index >= rows.length - 1 && !allLoaded && !loading)
      loadPage(nextPage)
  }, [virtualItems, rows.length, allLoaded, loading, loadPage, nextPage])

  // ── Fetch history ─────────────────────────────────────────────────────────

  const handleFetchHistory = async () => {
    if (!selectedGroupId || fetching) return
    setFetching(true)
    setFetchMsg(null)
    try {
      const { jobId } = await api.auditLog.fetchHistory(selectedGroupId)
      setFetchJobId(jobId)
      setFetchMsg(`History fetch started (job #${jobId}). Progress visible in the jobs panel.`)
    } catch (err) {
      setFetchMsg(err instanceof ApiError ? err.message : 'Failed to start fetch')
    } finally {
      setFetching(false)
    }
  }

  // ── Export ────────────────────────────────────────────────────────────────

  const handleExport = () => {
    if (!selectedGroupId) return
    const url = api.auditLog.exportUrl(selectedGroupId, {
      search:   search   || undefined,
      type:     typeFilter || undefined,
      from:     fromDate || undefined,
      to:       toDate   || undefined,
    })
    window.location.href = url
  }

  // ── No group ──────────────────────────────────────────────────────────────

  if (!selectedGroupId) return <NoGroupSelected />

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col h-full gap-3 min-h-0">

      {/* Header row */}
      <div className="flex items-center justify-between shrink-0">
        <div className="flex items-center gap-2">
          <h1 className="text-lg font-semibold text-white">Audit Log</h1>
          {total > 0 && (
            <span className="text-xs text-gray-500 tabular-nums">{total.toLocaleString()} entries</span>
          )}
          {/* Live indicator */}
          <span className={`flex items-center gap-1 text-xs ${connected ? 'text-emerald-400' : 'text-gray-600'}`}>
            <span className={`inline-block w-2 h-2 rounded-full ${connected ? 'bg-emerald-400 animate-pulse' : 'bg-gray-600'}`} />
            {connected ? 'Live' : 'Offline'}
          </span>
          {liveCount > 0 && (
            <span className="text-xs bg-emerald-900/50 text-emerald-300 border border-emerald-700 rounded px-1.5 py-0.5">
              +{liveCount} new
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={handleFetchHistory}
            disabled={fetching}
            className="px-3 py-1.5 text-xs rounded bg-gray-800 text-gray-300 border border-gray-700
                       hover:bg-gray-700 hover:text-white transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {fetching ? 'Starting…' : 'Fetch History'}
          </button>
          <button
            onClick={handleExport}
            className="px-3 py-1.5 text-xs rounded bg-gray-800 text-gray-300 border border-gray-700
                       hover:bg-gray-700 hover:text-white transition-colors"
          >
            Export CSV
          </button>
        </div>
      </div>

      {/* Fetch history feedback */}
      {fetchMsg && (
        <div className={`shrink-0 px-3 py-2 rounded text-xs border
          ${fetchJobId != null
            ? 'bg-blue-900/30 text-blue-300 border-blue-700'
            : 'bg-red-900/30 text-red-300 border-red-700'}`}>
          {fetchMsg}
        </div>
      )}

      {/* Filter bar */}
      <div className="shrink-0 flex flex-wrap gap-2 items-center">
        {/* Search */}
        <input
          type="search"
          value={searchInput}
          onChange={e => setSearchInput(e.target.value)}
          placeholder="Search actor, target, description…"
          className="w-64 px-3 py-1.5 text-sm rounded bg-gray-800 border border-gray-700
                     text-gray-100 placeholder-gray-500 focus:outline-none focus:border-blue-500"
        />

        {/* Type filter */}
        <input
          type="text"
          value={typeFilter}
          onChange={e => setTypeFilter(e.target.value)}
          placeholder="Action type (e.g. group.member.ban)"
          className="w-56 px-3 py-1.5 text-sm rounded bg-gray-800 border border-gray-700
                     text-gray-100 placeholder-gray-500 focus:outline-none focus:border-blue-500"
        />

        {/* Date range */}
        <label className="flex items-center gap-1 text-xs text-gray-400">
          From
          <input
            type="date"
            value={fromDate}
            onChange={e => setFromDate(e.target.value)}
            className="ml-1 px-2 py-1 text-sm rounded bg-gray-800 border border-gray-700
                       text-gray-100 focus:outline-none focus:border-blue-500"
          />
        </label>
        <label className="flex items-center gap-1 text-xs text-gray-400">
          To
          <input
            type="date"
            value={toDate}
            onChange={e => setToDate(e.target.value)}
            className="ml-1 px-2 py-1 text-sm rounded bg-gray-800 border border-gray-700
                       text-gray-100 focus:outline-none focus:border-blue-500"
          />
        </label>

        {/* Clear filters */}
        {(searchInput || typeFilter || fromDate || toDate) && (
          <button
            onClick={() => { setSearchInput(''); setTypeFilter(''); setFromDate(''); setToDate('') }}
            className="text-xs text-gray-500 hover:text-gray-300 transition-colors underline"
          >
            Clear filters
          </button>
        )}
      </div>

      {/* Error */}
      {loadErr && (
        <div className="shrink-0 px-3 py-2 rounded bg-red-900/30 text-red-300 border border-red-700 text-sm">
          {loadErr}
        </div>
      )}

      {/* Table header */}
      <div
        className="shrink-0 grid text-xs font-medium text-gray-500 border-b border-gray-800 pb-1 select-none"
        style={{ gridTemplateColumns: '160px 1fr 180px 180px 1fr 60px' }}
      >
        <span>Timestamp</span>
        <span>Action</span>
        <span>Actor</span>
        <span>Target</span>
        <span>Description</span>
        <span className="text-right">Source</span>
      </div>

      {/* Virtualized rows */}
      <div ref={parentRef} className="flex-1 overflow-y-auto min-h-0">
        {/* Empty / loading states */}
        {rows.length === 0 && !loading && !loadErr && (
          <div className="flex items-center justify-center py-20 text-gray-500 text-sm">
            No audit log entries found.{' '}
            <button
              onClick={handleFetchHistory}
              className="ml-2 text-blue-400 hover:text-blue-300 underline"
            >
              Fetch history from VRChat
            </button>
          </div>
        )}

        {rows.length === 0 && loading && (
          <div className="flex items-center justify-center py-20 text-gray-500 text-sm animate-pulse">
            Loading…
          </div>
        )}

        {rows.length > 0 && (
          <div style={{ height: totalListSize, position: 'relative' }}>
            {virtualItems.map(vItem => {
              const row = rows[vItem.index]
              return (
                <div
                  key={vItem.key}
                  data-index={vItem.index}
                  ref={virtualizer.measureElement}
                  className="items-center text-sm border-b border-gray-800/50 hover:bg-gray-800/30 transition-colors"
                  style={{
                    position: 'absolute',
                    top:    0,
                    left:   0,
                    right:  0,
                    transform: `translateY(${vItem.start}px)`,
                    height: ROW_HEIGHT,
                    display: 'grid',
                    gridTemplateColumns: '160px 1fr 180px 180px 1fr 60px',
                    alignItems: 'center',
                  }}
                >
                  {/* Timestamp */}
                  <span className="text-xs text-gray-400 tabular-nums truncate pr-2">
                    {fmtTs(row.timestamp)}
                  </span>

                  {/* Action */}
                  <span className="text-xs text-blue-300 font-mono truncate pr-2">
                    {actionLabel(row.action)}
                  </span>

                  {/* Actor */}
                  <div className="flex flex-col pr-2 min-w-0">
                    {row.actorDisplayName && (
                      <span className="text-sm text-gray-100 truncate leading-tight">{row.actorDisplayName}</span>
                    )}
                    {row.actorId && (
                      <span className="text-[10px] text-gray-500 truncate leading-tight font-mono">{row.actorId}</span>
                    )}
                    {!row.actorId && !row.actorDisplayName && <span className="text-gray-600">—</span>}
                  </div>

                  {/* Target */}
                  <div className="flex flex-col pr-2 min-w-0">
                    {row.targetDisplayName && (
                      <span className="text-sm text-gray-100 truncate leading-tight">{row.targetDisplayName}</span>
                    )}
                    {row.targetId && (
                      <span className="text-[10px] text-gray-500 truncate leading-tight font-mono">{row.targetId}</span>
                    )}
                    {!row.targetId && !row.targetDisplayName && <span className="text-gray-600">—</span>}
                  </div>

                  {/* Description */}
                  <span className="text-xs text-gray-400 truncate pr-2">
                    {row.description ?? '—'}
                  </span>

                  {/* Source badge */}
                  <div className="flex justify-end">
                    {sourceBadge(row.source)}
                  </div>
                </div>
              )
            })}
          </div>
        )}

        {/* Load-more sentinel */}
        {loading && rows.length > 0 && (
          <div className="flex justify-center py-4 text-xs text-gray-500 animate-pulse">
            Loading more…
          </div>
        )}

        {allLoaded && rows.length > 0 && (
          <div className="flex justify-center py-4 text-xs text-gray-600">
            All {total.toLocaleString()} entries loaded
          </div>
        )}
      </div>
    </div>
  )
}
