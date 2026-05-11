import { useRef, useState } from 'react'
import type { LocalUserDto } from '../types'
import { useGroups } from '../context/GroupContext'
import { useJobs } from '../context/JobsContext'
import { ApiError } from '../api/client'

interface TopBarProps {
  user: LocalUserDto
  onLogout: () => void
}

const roleBadgeClass: Record<string, string> = {
  Owner:        'bg-yellow-500/15 text-yellow-400 border-yellow-500/30',
  Admin:        'bg-red-500/15    text-red-400    border-red-500/30',
  Moderator:    'bg-purple-500/15 text-purple-400 border-purple-500/30',
  EventManager: 'bg-blue-500/15   text-blue-400   border-blue-500/30',
  Viewer:       'bg-gray-500/15   text-gray-400   border-gray-500/30',
}

export default function TopBar({ user, onLogout }: TopBarProps) {
  const badgeCls = roleBadgeClass[user.role] ?? roleBadgeClass['Viewer']
  const { groups, selectedGroupId, setSelectedGroupId, addGroup, loading } = useGroups()
  const { jobs, connected, setDrawerOpen } = useJobs()

  const activeJobCount = [...jobs.values()].filter(
    j => j.status === 'Pending' || j.status === 'Running' || j.status === 'Cancelling'
  ).length

  // Add-group popover state
  const [showAdd,   setShowAdd]   = useState(false)
  const [addInput,  setAddInput]  = useState('')
  const [addBusy,   setAddBusy]   = useState(false)
  const [addError,  setAddError]  = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const openAdd = () => {
    setAddInput('')
    setAddError(null)
    setShowAdd(true)
    setTimeout(() => inputRef.current?.focus(), 50)
  }

  const cancelAdd = () => {
    setShowAdd(false)
    setAddError(null)
  }

  const submitAdd = async () => {
    const id = addInput.trim()
    if (!id) return
    setAddBusy(true)
    setAddError(null)
    try {
      await addGroup(id)
      setShowAdd(false)
      setAddInput('')
    } catch (e) {
      setAddError(
        e instanceof ApiError
          ? (e.code === 'already_managed' ? 'Group already added.' : e.message)
          : 'Failed to add group.'
      )
    } finally {
      setAddBusy(false)
    }
  }

  const selectedGroup = groups.find(g => g.groupId === selectedGroupId)

  return (
    <header className="h-14 shrink-0 flex items-center justify-between px-5 border-b border-gray-800 bg-gray-900 relative">

      {/* Left: group selector */}
      <div className="flex items-center gap-2">
        {loading && groups.length === 0 ? (
          <div className="h-7 w-40 bg-gray-800 rounded animate-pulse" />
        ) : groups.length === 0 ? (
          <span className="text-sm text-gray-500 italic">No groups configured</span>
        ) : (
          <select
            value={selectedGroupId ?? ''}
            onChange={e => setSelectedGroupId(e.target.value || null)}
            className="text-sm bg-gray-800 border border-gray-700 text-gray-200 rounded-lg px-3 py-1 focus:outline-none focus:ring-1 focus:ring-violet-500 max-w-xs"
          >
            {groups.map(g => (
              <option key={g.groupId} value={g.groupId}>
                {g.name}{g.shortCode ? ` · ${g.shortCode}` : ''}
              </option>
            ))}
          </select>
        )}

        {/* Add group button / inline popover */}
        {!showAdd ? (
          <button
            onClick={openAdd}
            title="Add group"
            className="text-xs text-gray-500 hover:text-violet-400 transition-colors px-2 py-1 rounded hover:bg-gray-800 border border-transparent hover:border-gray-700"
          >
            + Add group
          </button>
        ) : (
          <div className="flex items-center gap-2">
            <input
              ref={inputRef}
              value={addInput}
              onChange={e => setAddInput(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') submitAdd(); if (e.key === 'Escape') cancelAdd() }}
              placeholder="grp_xxxxxxxx-…"
              disabled={addBusy}
              className="text-xs bg-gray-800 border border-gray-600 text-gray-200 rounded-lg px-3 py-1 w-64 focus:outline-none focus:ring-1 focus:ring-violet-500 disabled:opacity-50"
            />
            <button
              onClick={submitAdd}
              disabled={addBusy || !addInput.trim()}
              className="text-xs bg-violet-600 hover:bg-violet-500 disabled:opacity-40 text-white rounded-lg px-3 py-1 transition-colors"
            >
              {addBusy ? 'Adding…' : 'Add'}
            </button>
            <button
              onClick={cancelAdd}
              className="text-xs text-gray-500 hover:text-gray-300 transition-colors px-2 py-1"
            >
              Cancel
            </button>
            {addError && (
              <span className="text-xs text-red-400">{addError}</span>
            )}
          </div>
        )}
      </div>

      {/* Right: user info + logout */}
      <div className="flex items-center gap-3">
        {selectedGroup && (
          <span className="text-xs text-gray-500 font-mono truncate max-w-[180px] hidden sm:block">
            {selectedGroup.groupId}
          </span>
        )}

        {/* Jobs indicator */}
        <button
          onClick={() => setDrawerOpen(true)}
          title={connected ? 'Background jobs' : 'Background jobs (disconnected)'}
          className="relative text-xs text-gray-400 hover:text-violet-300 transition-colors px-2 py-1 rounded hover:bg-gray-800"
        >
          <span>⚙</span>
          {activeJobCount > 0 && (
            <span className="absolute -top-0.5 -right-0.5 h-3.5 w-3.5 flex items-center justify-center rounded-full bg-violet-600 text-[9px] font-bold text-white">
              {activeJobCount > 9 ? '9+' : activeJobCount}
            </span>
          )}
          {!connected && (
            <span className="absolute -bottom-0.5 -right-0.5 h-2 w-2 rounded-full bg-red-500" title="SignalR disconnected" />
          )}
        </button>
        <span className={`text-xs border rounded-full px-2 py-0.5 ${badgeCls}`}>
          {user.role}
        </span>
        <span className="text-sm text-gray-300 font-medium">{user.displayName}</span>
        <button
          onClick={onLogout}
          className="text-xs text-gray-500 hover:text-gray-200 transition-colors px-2 py-1 rounded hover:bg-gray-800"
        >
          Sign out
        </button>
      </div>
    </header>
  )
}
