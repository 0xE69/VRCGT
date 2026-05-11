import type { LocalUserDto, SetupStatusDto, StorageDto, VersionDto, GroupDetailDto, MemberPageDto, GroupMemberDto, JobSummaryDto, AuditLogPageDto, BadgeScanSummaryDto, BadgeScanDetailDto, KillSwitchPreviewDto, KillSwitchSnapshotDto, KillSwitchExecuteResultDto, AccessSettingsDto } from '../types'

// ── core fetch wrapper ───────────────────────────────────────────────────────

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body:    body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!res.ok) {
    const payload = await res.json().catch(() => ({ error: `http_${res.status}` })) as { error?: string; message?: string }
    const code    = payload.error ?? `http_${res.status}`
    const message = payload.message ?? payload.error ?? res.statusText
    throw new ApiError(res.status, code, message)
  }

  // 204 No Content has no body
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

const get  = <T>(path: string)                => request<T>('GET',    path)
const post = <T>(path: string, body?: unknown) => request<T>('POST',   path, body ?? {})
const patch = <T>(path: string, body?: unknown) => request<T>('PATCH', path, body ?? {})
const del  = <T>(path: string, body?: unknown) => request<T>('DELETE', path, body)

// ── Confirmation token helpers ─────────────────────────────────────────────────

/** Build a confirmation token matching ConfirmationGuard.BuildToken on the server. */
export function buildConfirmToken(action: string, resourceId: string): string {
  return `${action}:${resourceId}`
}

export const ConfirmActions = {
  DeleteGroup:       'DELETE_GROUP',
  RefreshMembers:    'REFRESH_MEMBERS',
  KickMember:        'KICK_MEMBER',
  BanMember:         'BAN_MEMBER',
  UnbanMember:       'UNBAN_MEMBER',
  RemoveRoles:       'REMOVE_ROLES',
  KickAllUnverified: 'KICK_ALL_UNVERIFIED',
  DeleteBackup:      'DELETE_BACKUP',
  RestoreBackup:     'RESTORE_BACKUP',
  CloseInstance:     'CLOSE_INSTANCE',
  DeletePost:        'DELETE_POST',
  DeleteEvent:       'DELETE_EVENT',
} as const

// ── API surface ──────────────────────────────────────────────────────────────

export const api = {
  setup: {
    status() {
      return get<SetupStatusDto>('/api/setup/status')
    },
    createOwner(username: string, password: string, displayName: string) {
      return post<LocalUserDto>('/api/setup/owner', { username, password, displayName })
    },
  },

  auth: {
    login(username: string, password: string) {
      return post<LocalUserDto>('/api/auth/login', { username, password })
    },
    logout() {
      return post<void>('/api/auth/logout')
    },
    me() {
      return get<LocalUserDto>('/api/auth/me')
    },
  },

  system: {
    storage() { return get<StorageDto>('/api/system/storage') },
    version() { return get<VersionDto>('/api/v2/version') },
  },

  groups: {
    list()                  { return get<GroupDetailDto[]>('/api/groups/') },
    get(groupId: string)    { return get<GroupDetailDto>(`/api/groups/${encodeURIComponent(groupId)}`) },
    add(groupId: string)    { return post<GroupDetailDto>('/api/groups/', { groupId }) },
    refresh(groupId: string){ return post<GroupDetailDto>(`/api/groups/${encodeURIComponent(groupId)}/refresh`) },
    remove(groupId: string) {
      return del<void>(
        `/api/groups/${encodeURIComponent(groupId)}`,
        { confirmToken: buildConfirmToken(ConfirmActions.DeleteGroup, groupId) }
      )
    },
  },

  members: {
    list(groupId: string, params: {
      page?: number; pageSize?: number; search?: string; role?: string
    } = {}) {
      const q = new URLSearchParams()
      if (params.page     != null) q.set('page',     String(params.page))
      if (params.pageSize != null) q.set('pageSize', String(params.pageSize))
      if (params.search)           q.set('search',   params.search)
      if (params.role)             q.set('role',     params.role)
      const qs = q.toString()
      return get<MemberPageDto>(
        `/api/groups/${encodeURIComponent(groupId)}/members/${qs ? `?${qs}` : ''}`)
    },
    get(groupId: string, userId: string) {
      return get<{ member: GroupMemberDto; source: string; cacheAgeSeconds: number }>(
        `/api/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(userId)}`)
    },
    refresh(groupId: string) {
      return post<{ count: number; refreshedAt: string; source: string }>(
        `/api/groups/${encodeURIComponent(groupId)}/members/refresh`,
        { confirmToken: buildConfirmToken(ConfirmActions.RefreshMembers, groupId) }
      )
    },
  },

  jobs: {
    list()                      { return get<JobSummaryDto[]>('/api/jobs') },
    get(jobId: number)          { return get<JobSummaryDto>(`/api/jobs/${jobId}`) },
    cancel(jobId: number)       { return post<JobSummaryDto>(`/api/jobs/${jobId}/cancel`) },
    createTest(steps?: number)  {
      const qs = steps != null ? `?steps=${steps}` : ''
      return post<JobSummaryDto>(`/api/jobs/test${qs}`)
    },
  },

  auditLog: {
    list(groupId: string, params: {
      page?: number; pageSize?: number; search?: string; type?: string;
      from?: string; to?: string
    } = {}) {
      const q = new URLSearchParams()
      if (params.page     != null) q.set('page',     String(params.page))
      if (params.pageSize != null) q.set('pageSize', String(params.pageSize))
      if (params.search)           q.set('search',   params.search)
      if (params.type)             q.set('type',     params.type)
      if (params.from)             q.set('from',     params.from)
      if (params.to)               q.set('to',       params.to)
      const qs = q.toString()
      return get<AuditLogPageDto>(
        `/api/groups/${encodeURIComponent(groupId)}/audit-logs${qs ? `?${qs}` : ''}`)
    },

    fetchHistory(groupId: string) {
      return post<{ jobId: number }>(
        `/api/groups/${encodeURIComponent(groupId)}/audit-logs/fetch-history`)
    },

    exportUrl(groupId: string, params: {
      search?: string; type?: string; from?: string; to?: string
    } = {}) {
      const q = new URLSearchParams()
      if (params.search) q.set('search', params.search)
      if (params.type)   q.set('type',   params.type)
      if (params.from)   q.set('from',   params.from)
      if (params.to)     q.set('to',     params.to)
      const qs = q.toString()
      return `/api/groups/${encodeURIComponent(groupId)}/audit-logs/export${qs ? `?${qs}` : ''}`
    },
  },

  badgeScan: {
    start(groupId: string) {
      return post<BadgeScanSummaryDto>(
        `/api/groups/${encodeURIComponent(groupId)}/badge-scans`)
    },

    latest(groupId: string) {
      return get<BadgeScanSummaryDto>(
        `/api/groups/${encodeURIComponent(groupId)}/badge-scans/latest`)
        .catch(() => null as BadgeScanSummaryDto | null)
    },

    get(groupId: string, scanId: number) {
      return get<BadgeScanDetailDto>(
        `/api/groups/${encodeURIComponent(groupId)}/badge-scans/${scanId}`)
    },

    kickSelected(groupId: string, scanId: number, userIds: string[], confirmToken: string) {
      return post<{ jobId: number }>(
        `/api/groups/${encodeURIComponent(groupId)}/badge-scans/${scanId}/kick-selected`,
        { confirmToken, userIds })
    },

    kickUnverified(groupId: string, scanId: number, confirmToken: string) {
      return post<{ jobId: number }>(
        `/api/groups/${encodeURIComponent(groupId)}/badge-scans/${scanId}/kick-unverified`,
        { confirmToken })
    },

    exportUrl(groupId: string, scanId: number) {
      return `/api/groups/${encodeURIComponent(groupId)}/badge-scans/${scanId}/export`
    },
  },

  killSwitch: {
    preview(groupId: string) {
      return get<KillSwitchPreviewDto>(
        `/api/groups/${encodeURIComponent(groupId)}/kill-switch/preview`)
    },

    refresh(groupId: string) {
      return post<KillSwitchPreviewDto>(
        `/api/groups/${encodeURIComponent(groupId)}/kill-switch/refresh`)
    },

    execute(groupId: string, roleIds: string[], userIds: string[], confirmToken: string) {
      return post<KillSwitchExecuteResultDto>(
        `/api/groups/${encodeURIComponent(groupId)}/kill-switch/execute`,
        { confirmToken, roleIds, userIds })
    },

    snapshots(groupId: string) {
      return get<KillSwitchSnapshotDto[]>(
        `/api/groups/${encodeURIComponent(groupId)}/kill-switch/snapshots`)
    },

    restore(groupId: string, snapshotId: number, confirmToken: string) {
      return post<{ jobId: number }>(
        `/api/groups/${encodeURIComponent(groupId)}/kill-switch/snapshots/${snapshotId}/restore`,
        { confirmToken })
    },
  },

  localUsers: {
    list() {
      return get<LocalUserDto[]>('/api/local-users/')
    },
    create(username: string, password: string, displayName: string, role: string) {
      return post<LocalUserDto>('/api/local-users/', { username, password, displayName, role })
    },
    update(id: number, data: { displayName?: string; role?: string; isEnabled?: boolean }) {
      return patch<LocalUserDto>(`/api/local-users/${id}`, data)
    },
    delete(id: number) {
      return del<void>(`/api/local-users/${id}`)
    },
    resetPassword(id: number, newPassword: string) {
      return post<{ message: string }>(`/api/local-users/${id}/reset-password`, { newPassword })
    },
  },

  systemAccess: {
    get() {
      return get<AccessSettingsDto>('/api/system/access')
    },
    update(bindMode: 'LocalOnly' | 'Lan', confirmToken?: string) {
      return patch<AccessSettingsDto>('/api/system/access', { bindMode, confirmToken })
    },
  },
}
