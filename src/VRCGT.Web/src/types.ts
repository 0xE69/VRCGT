// DTO shapes that mirror the backend LocalUserDto and other response types.

export type LocalUserRole = 'Owner' | 'Admin' | 'Moderator' | 'EventManager' | 'Viewer'

export interface LocalUserDto {
  id: number
  username: string
  displayName: string
  role: LocalUserRole
  isEnabled: boolean
  createdAt: string
  lastLoginAt: string | null
  vrchatUserId: string | null
  vrchatDisplayName: string | null
  permissions: string[]
}

export interface SetupStatusDto {
  setupRequired: boolean
}

export interface StorageDto {
  appDataPath: string
  dataPath: string
  logsPath: string
  cachePath: string
  exportsPath: string
  backupsPath: string
  databasePath: string
  databaseExists: boolean
}

export interface VersionDto {
  app: string
  version: string
  runtime: string
  os: string
  arch: string
  bindAddress: string
  port: string
  lanMode: boolean
}

export interface AccessSettingsDto {
  savedBindMode: 'LocalOnly' | 'Lan'
  runtimeBindMode: 'LocalOnly' | 'Lan'
  port: number
  requiresRestart: boolean
  listenUrls: string[]
  lanAddresses: string[]
}

// ── Group management ──────────────────────────────────────────────────────────

export interface ManagedGroupDto {
  groupId: string
  name: string
  shortCode: string | null
  description: string | null
  iconUrl: string | null
  bannerUrl: string | null
  isActive: boolean
  memberCount: number
  onlineCount: number
  addedAt: string
  lastRefreshedAt: string | null
}

/** ManagedGroupDto extended with freshness metadata from the server. */
export interface GroupDetailDto extends ManagedGroupDto {
  source: 'live' | 'cache'
  cacheAgeSeconds: number | null
}

// ── Member cache ──────────────────────────────────────────────────────────────

export interface GroupMemberDto {
  groupId: string
  userId: string
  displayName: string
  joinedAt: string | null
  roleIds: string[]
  roleNames: string[]
  avatarUrl: string | null
  isRepresenting: boolean
  cachedAt: string
}

export interface MemberPageDto {
  items: GroupMemberDto[]
  totalCount: number
  page: number
  pageSize: number
  source: 'live' | 'cache'
  lastRefreshedAt: string | null
  cacheAgeSeconds: number | null
}

// ── Background jobs ───────────────────────────────────────────────────────────

export type BackgroundJobStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'Cancelling'

export interface JobSummaryDto {
  id: number
  jobType: string
  label: string | null
  status: BackgroundJobStatus
  processedItems: number
  totalItems: number
  failedItems: number
  currentItemLabel: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  errorMessage: string | null
}

/** Live progress event broadcast over SignalR. */
export interface JobProgressDto {
  jobId: number
  jobType: string
  label: string | null
  status: BackgroundJobStatus
  processed: number
  total: number
  successCount: number
  failureCount: number
  currentItem: string | null
  startedAt: string | null
  completedAt: string | null
  errorMessage: string | null
}

// ── Audit log ─────────────────────────────────────────────────────────────────

export interface AuditLogEntryDto {
  id: number
  groupId: string
  timestamp: string
  action: string
  actorId: string | null
  actorDisplayName: string | null
  targetId: string | null
  targetDisplayName: string | null
  description: string | null
  source: 'vrchat' | 'vrcgt'
  fetchedAt: string
}

export interface AuditLogPageDto {
  items: AuditLogEntryDto[]
  totalCount: number
  page: number
  pageSize: number
  latestId: number | null
  latestTimestamp: string | null
}

// ── Badge scanner ─────────────────────────────────────────────────────────────

export type BadgeScanStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'

export interface BadgeScanMemberDto {
  id: number
  userId: string
  displayName: string
  isAgeVerified: boolean | null   // null = unknown
  wasKicked: boolean
  scannedAt: string
}

export interface BadgeScanSummaryDto {
  id: number
  groupId: string
  status: BadgeScanStatus
  createdAt: string
  completedAt: string | null
  totalMembers: number
  scannedMembers: number
  verifiedCount: number
  unverifiedCount: number
  unknownCount: number
  kickedCount: number
  jobId: number | null
  errorMessage: string | null
}

export interface BadgeScanDetailDto extends BadgeScanSummaryDto {
  members: BadgeScanMemberDto[]
}

// ── Kill Switch ───────────────────────────────────────────────────────────────

export interface GroupRoleDto {
  roleId: string
  name: string
  description: string | null
  permissions: string[]
}

export interface KillSwitchMemberPreviewDto {
  userId: string
  displayName: string
  roleIds: string[]
  roleNames: string[]
}

export interface KillSwitchPreviewDto {
  memberCount: number
  roleAssignmentCount: number
  cacheTimestamp: string | null
  isStale: boolean
  affectedMembers: KillSwitchMemberPreviewDto[]
  availableRoles: GroupRoleDto[]
}

export interface KillSwitchSnapshotDto {
  id: number
  groupId: string
  createdAt: string
  memberCount: number
  roleAssignmentCount: number
  restoredCount: number
  restoredAt: string | null
  label: string | null
}

export interface KillSwitchExecuteResultDto {
  snapshotId: number
  jobId: number
}
