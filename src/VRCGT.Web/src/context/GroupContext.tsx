import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react'
import { api } from '../api/client'
import type { GroupDetailDto } from '../types'

// ── Context shape ─────────────────────────────────────────────────────────────

interface GroupContextValue {
  /** All managed groups, ordered by isActive desc then addedAt asc. */
  groups: GroupDetailDto[]
  /** ID of the group currently shown in the dashboard. */
  selectedGroupId: string | null
  setSelectedGroupId: (id: string | null) => void
  /** Add a group by VRChat group ID (verifies via API). */
  addGroup: (groupId: string) => Promise<GroupDetailDto>
  /** Remove a group from the managed set. */
  removeGroup: (groupId: string) => Promise<void>
  /** Re-fetch group data from VRChat and update the cache. */
  refreshGroup: (groupId: string) => Promise<GroupDetailDto>
  /** Reload the full group list from the server. */
  reload: () => Promise<void>
  loading: boolean
  error: string | null
}

const GroupContext = createContext<GroupContextValue | null>(null)

// ── Provider ──────────────────────────────────────────────────────────────────

export function GroupProvider({ children }: { children: ReactNode }) {
  const [groups,          setGroups]          = useState<GroupDetailDto[]>([])
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(null)
  const [loading,         setLoading]         = useState(true)
  const [error,           setError]           = useState<string | null>(null)

  const reload = useCallback(async () => {
    setLoading(true)
    try {
      const list = await api.groups.list()
      setGroups(list)
      // Auto-select: keep current if still present; otherwise use server-active or first
      setSelectedGroupId(prev => {
        if (prev && list.some(g => g.groupId === prev)) return prev
        return (list.find(g => g.isActive) ?? list[0])?.groupId ?? null
      })
      setError(null)
    } catch {
      setError('Failed to load groups.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { reload() }, [reload])

  const addGroup = useCallback(async (groupId: string) => {
    const dto = await api.groups.add(groupId)
    // Reload to get the updated list; auto-select the new group if it's the first
    await reload()
    setSelectedGroupId(id => id ?? dto.groupId)
    return dto
  }, [reload])

  const removeGroup = useCallback(async (groupId: string) => {
    await api.groups.remove(groupId)
    await reload()
  }, [reload])

  const refreshGroup = useCallback(async (groupId: string) => {
    const dto = await api.groups.refresh(groupId)
    setGroups(prev => prev.map(g => g.groupId === groupId ? dto : g))
    return dto
  }, [])

  return (
    <GroupContext.Provider value={{
      groups, selectedGroupId, setSelectedGroupId,
      addGroup, removeGroup, refreshGroup, reload,
      loading, error,
    }}>
      {children}
    </GroupContext.Provider>
  )
}

// ── Hook ──────────────────────────────────────────────────────────────────────

export function useGroups(): GroupContextValue {
  const ctx = useContext(GroupContext)
  if (!ctx) throw new Error('useGroups must be called inside <GroupProvider>')
  return ctx
}
