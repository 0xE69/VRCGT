import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { LocalUserDto, LocalUserRole } from '../types'

// ── Constants ─────────────────────────────────────────────────────────────────

const ROLES: LocalUserRole[] = ['Owner', 'Admin', 'Moderator', 'EventManager', 'Viewer']

const ROLE_COLORS: Record<LocalUserRole, string> = {
  Owner:        'bg-red-900/60 text-red-300 border-red-700',
  Admin:        'bg-orange-900/60 text-orange-300 border-orange-700',
  Moderator:    'bg-blue-900/60 text-blue-300 border-blue-700',
  EventManager: 'bg-purple-900/60 text-purple-300 border-purple-700',
  Viewer:       'bg-gray-700/60 text-gray-300 border-gray-600',
}

const PERM_COLORS: Record<string, string> = {
  UseKillSwitch:   'text-red-400',
  ManageLocalUsers:'text-red-400',
  ManageSecurity:  'text-orange-400',
  BanMembers:      'text-orange-400',
  KickMembers:     'text-yellow-400',
  ManagePosts:     'text-yellow-400',
  ManageEvents:    'text-purple-400',
  ManageWebhooks:  'text-purple-400',
  ExportData:      'text-blue-400',
}

function fmtDate(iso: string | null | undefined) {
  if (!iso) return '—'
  try { return new Date(iso).toLocaleString() } catch { return iso }
}

// ── Modal: Create / Edit user ─────────────────────────────────────────────────

interface UserFormProps {
  mode: 'create' | 'edit'
  initial?: LocalUserDto
  onSave: (data: UserFormData) => Promise<void>
  onClose: () => void
}

interface UserFormData {
  username: string
  password: string
  displayName: string
  role: LocalUserRole
  isEnabled: boolean
}

function UserFormModal({ mode, initial, onSave, onClose }: UserFormProps) {
  const [username,    setUsername]    = useState(initial?.username    ?? '')
  const [displayName, setDisplayName] = useState(initial?.displayName ?? '')
  const [password,    setPassword]    = useState('')
  const [role,        setRole]        = useState<LocalUserRole>(initial?.role ?? 'Viewer')
  const [isEnabled,   setIsEnabled]   = useState(initial?.isEnabled ?? true)
  const [saving,      setSaving]      = useState(false)
  const [error,       setError]       = useState<string | null>(null)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true); setError(null)
    try {
      await onSave({ username, password, displayName, role, isEnabled })
    } catch (err: any) {
      setError(err?.message ?? 'Save failed')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70">
      <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-xl w-full max-w-md mx-4">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <h2 className="text-base font-semibold text-white">
            {mode === 'create' ? 'Create User' : `Edit — ${initial?.username}`}
          </h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        <form onSubmit={handleSubmit} className="px-6 py-5 flex flex-col gap-4">
          {error && (
            <div className="text-sm text-red-400 rounded border border-red-700 bg-red-950/40 px-3 py-2">
              {error}
            </div>
          )}

          {mode === 'create' && (
            <label className="flex flex-col gap-1">
              <span className="text-xs text-gray-400 font-medium">Username</span>
              <input
                type="text"
                value={username}
                onChange={e => setUsername(e.target.value)}
                required
                autoFocus
                autoComplete="off"
                className="rounded bg-gray-800 border border-gray-600 px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
              />
            </label>
          )}

          <label className="flex flex-col gap-1">
            <span className="text-xs text-gray-400 font-medium">Display Name</span>
            <input
              type="text"
              value={displayName}
              onChange={e => setDisplayName(e.target.value)}
              required
              className="rounded bg-gray-800 border border-gray-600 px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
            />
          </label>

          {mode === 'create' && (
            <label className="flex flex-col gap-1">
              <span className="text-xs text-gray-400 font-medium">Password</span>
              <input
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                required
                autoComplete="new-password"
                className="rounded bg-gray-800 border border-gray-600 px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
              />
              <span className="text-xs text-gray-500">Minimum 8 characters</span>
            </label>
          )}

          <label className="flex flex-col gap-1">
            <span className="text-xs text-gray-400 font-medium">Role</span>
            <select
              value={role}
              onChange={e => setRole(e.target.value as LocalUserRole)}
              className="rounded bg-gray-800 border border-gray-600 px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
            >
              {ROLES.map(r => (
                <option key={r} value={r}>{r}</option>
              ))}
            </select>
          </label>

          {mode === 'edit' && (
            <label className="flex items-center gap-3 cursor-pointer">
              <input
                type="checkbox"
                checked={isEnabled}
                onChange={e => setIsEnabled(e.target.checked)}
                className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-500"
              />
              <span className="text-sm text-gray-300">Account enabled</span>
            </label>
          )}

          <div className="flex gap-3 pt-2">
            <button
              type="submit"
              disabled={saving}
              className="flex-1 py-2 rounded bg-blue-700 hover:bg-blue-600 disabled:opacity-50 text-sm font-medium text-white transition-colors"
            >
              {saving ? 'Saving…' : mode === 'create' ? 'Create' : 'Save'}
            </button>
            <button
              type="button"
              onClick={onClose}
              className="flex-1 py-2 rounded bg-gray-700 hover:bg-gray-600 text-sm font-medium text-white transition-colors"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Modal: Reset Password ─────────────────────────────────────────────────────

interface ResetPasswordModalProps {
  target: LocalUserDto
  onSave: (newPassword: string) => Promise<void>
  onClose: () => void
}

function ResetPasswordModal({ target, onSave, onClose }: ResetPasswordModalProps) {
  const [password, setPassword] = useState('')
  const [confirm,  setConfirm]  = useState('')
  const [saving,   setSaving]   = useState(false)
  const [error,    setError]    = useState<string | null>(null)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (password !== confirm) { setError('Passwords do not match.'); return }
    setSaving(true); setError(null)
    try {
      await onSave(password)
    } catch (err: any) {
      setError(err?.message ?? 'Reset failed')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70">
      <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-xl w-full max-w-sm mx-4">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <h2 className="text-base font-semibold text-white">Reset Password — {target.username}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        <form onSubmit={handleSubmit} className="px-6 py-5 flex flex-col gap-4">
          {error && (
            <div className="text-sm text-red-400 rounded border border-red-700 bg-red-950/40 px-3 py-2">
              {error}
            </div>
          )}

          <label className="flex flex-col gap-1">
            <span className="text-xs text-gray-400 font-medium">New Password</span>
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              required
              autoFocus
              autoComplete="new-password"
              className="rounded bg-gray-800 border border-gray-600 px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-xs text-gray-400 font-medium">Confirm Password</span>
            <input
              type="password"
              value={confirm}
              onChange={e => setConfirm(e.target.value)}
              required
              autoComplete="new-password"
              className="rounded bg-gray-800 border border-gray-600 px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
            />
          </label>

          <div className="flex gap-3 pt-2">
            <button
              type="submit"
              disabled={saving}
              className="flex-1 py-2 rounded bg-orange-700 hover:bg-orange-600 disabled:opacity-50 text-sm font-medium text-white transition-colors"
            >
              {saving ? 'Resetting…' : 'Reset Password'}
            </button>
            <button
              type="button"
              onClick={onClose}
              className="flex-1 py-2 rounded bg-gray-700 hover:bg-gray-600 text-sm font-medium text-white transition-colors"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Permissions badge row ─────────────────────────────────────────────────────

function PermissionList({ perms }: { perms: string[] }) {
  return (
    <div className="flex flex-wrap gap-1 mt-2">
      {perms.map(p => (
        <span
          key={p}
          className={`text-xs px-1.5 py-0.5 rounded border border-gray-600 bg-gray-800 ${PERM_COLORS[p] ?? 'text-gray-400'}`}
        >
          {p}
        </span>
      ))}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function LocalUsersPage() {
  const [users,       setUsers]       = useState<LocalUserDto[]>([])
  const [loading,     setLoading]     = useState(true)
  const [error,       setError]       = useState<string | null>(null)
  const [expandedId,  setExpandedId]  = useState<number | null>(null)

  type ModalState =
    | { type: 'create' }
    | { type: 'edit'; user: LocalUserDto }
    | { type: 'reset'; user: LocalUserDto }
    | { type: 'delete'; user: LocalUserDto }
    | null

  const [modal, setModal] = useState<ModalState>(null)

  const load = useCallback(async () => {
    setLoading(true); setError(null)
    try { setUsers(await api.localUsers.list()) }
    catch (err: any) { setError(err?.message ?? 'Failed to load users') }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { load() }, [load])

  const ownerCount = users.filter(u => u.role === 'Owner' && u.isEnabled).length

  // ── Handlers ──────────────────────────────────────────────────────────────

  const handleCreate = async (data: UserFormData) => {
    const user = await api.localUsers.create(data.username, data.password, data.displayName, data.role)
    setUsers(prev => [...prev, user])
    setModal(null)
  }

  const handleEdit = async (data: UserFormData) => {
    if (modal?.type !== 'edit') return
    const user = await api.localUsers.update(modal.user.id, {
      displayName: data.displayName,
      role: data.role,
      isEnabled: data.isEnabled,
    })
    setUsers(prev => prev.map(u => u.id === user.id ? user : u))
    setModal(null)
  }

  const handleResetPassword = async (newPassword: string) => {
    if (modal?.type !== 'reset') return
    await api.localUsers.resetPassword(modal.user.id, newPassword)
    setModal(null)
  }

  const handleDelete = async () => {
    if (modal?.type !== 'delete') return
    try {
      await api.localUsers.delete(modal.user.id)
      setUsers(prev => prev.filter(u => u.id !== modal.user.id))
      setModal(null)
    } catch (err: any) {
      setError(err?.message ?? 'Delete failed')
      setModal(null)
    }
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col gap-6 px-6 py-6">

      {/* Header */}
      <div className="flex items-center justify-between flex-wrap gap-3">
        <div>
          <h1 className="text-lg font-bold text-white">Local Users</h1>
          <p className="text-sm text-gray-400 mt-0.5">
            Manage who can access this VRCGT installation.
            All accounts are local only — no cloud or VRChat accounts.
          </p>
        </div>
        <button
          onClick={() => setModal({ type: 'create' })}
          className="px-4 py-2 rounded bg-blue-700 hover:bg-blue-600 text-sm font-medium text-white transition-colors"
        >
          + Create User
        </button>
      </div>

      {/* Error */}
      {error && (
        <div className="rounded border border-red-700 bg-red-900/30 px-4 py-3 text-sm text-red-300">
          {error}
          <button onClick={() => setError(null)} className="ml-3 underline text-xs">Dismiss</button>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="text-center text-gray-400 py-12">Loading users…</div>
      )}

      {/* Users table */}
      {!loading && users.length === 0 && (
        <div className="text-center text-gray-500 py-12">No users found.</div>
      )}

      {!loading && users.length > 0 && (
        <div className="rounded-lg border border-gray-700 overflow-hidden">

          {/* Table header */}
          <div className="grid grid-cols-[1fr_1fr_120px_80px_200px] gap-0 bg-gray-800 px-4 py-2.5 border-b border-gray-700 text-xs font-medium text-gray-400 uppercase tracking-wide">
            <div>Username / Display Name</div>
            <div>Role</div>
            <div>Status</div>
            <div>Perms</div>
            <div className="text-right">Actions</div>
          </div>

          {/* Rows */}
          {users.map(user => {
            const isLastOwner = user.role === 'Owner' && ownerCount <= 1
            const expanded    = expandedId === user.id

            return (
              <div key={user.id} className="border-b border-gray-700 last:border-b-0">
                <div className="grid grid-cols-[1fr_1fr_120px_80px_200px] gap-0 items-center px-4 py-3 hover:bg-gray-800/50 transition-colors">

                  {/* Identity */}
                  <div className="min-w-0">
                    <div className="text-sm font-medium text-white truncate">{user.username}</div>
                    <div className="text-xs text-gray-400 truncate">{user.displayName}</div>
                    <div className="text-xs text-gray-600 mt-0.5">Last login: {fmtDate(user.lastLoginAt)}</div>
                  </div>

                  {/* Role badge */}
                  <div>
                    <span className={`inline-block text-xs font-medium px-2 py-0.5 rounded border ${ROLE_COLORS[user.role]}`}>
                      {user.role}
                    </span>
                  </div>

                  {/* Enabled / disabled */}
                  <div>
                    <span className={`text-xs font-medium ${user.isEnabled ? 'text-green-400' : 'text-gray-500'}`}>
                      {user.isEnabled ? '● Enabled' : '○ Disabled'}
                    </span>
                  </div>

                  {/* Permission count expand toggle */}
                  <div>
                    <button
                      onClick={() => setExpandedId(expanded ? null : user.id)}
                      className="text-xs text-gray-400 hover:text-white transition-colors"
                      title="Show effective permissions"
                    >
                      {user.permissions.length} {expanded ? '▲' : '▼'}
                    </button>
                  </div>

                  {/* Actions */}
                  <div className="flex items-center justify-end gap-2">
                    <button
                      onClick={() => setModal({ type: 'edit', user })}
                      className="px-2.5 py-1 rounded bg-gray-700 hover:bg-gray-600 text-xs text-white transition-colors"
                    >
                      Edit
                    </button>
                    <button
                      onClick={() => setModal({ type: 'reset', user })}
                      className="px-2.5 py-1 rounded bg-gray-700 hover:bg-gray-600 text-xs text-white transition-colors"
                    >
                      Reset PW
                    </button>
                    <button
                      onClick={() => setModal({ type: 'delete', user })}
                      disabled={isLastOwner}
                      title={isLastOwner ? 'Cannot delete the last Owner' : 'Delete user'}
                      className="px-2.5 py-1 rounded bg-gray-700 hover:bg-red-700 disabled:opacity-30 disabled:cursor-not-allowed text-xs text-white transition-colors"
                    >
                      Delete
                    </button>
                  </div>
                </div>

                {/* Expanded permissions */}
                {expanded && (
                  <div className="px-4 pb-3 bg-gray-800/40 border-t border-gray-700/50">
                    <div className="text-xs text-gray-500 mt-2 mb-1 font-medium uppercase tracking-wide">Effective permissions</div>
                    <PermissionList perms={user.permissions} />
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}

      {/* ── Modals ── */}

      {modal?.type === 'create' && (
        <UserFormModal
          mode="create"
          onSave={handleCreate}
          onClose={() => setModal(null)}
        />
      )}

      {modal?.type === 'edit' && (
        <UserFormModal
          mode="edit"
          initial={modal.user}
          onSave={handleEdit}
          onClose={() => setModal(null)}
        />
      )}

      {modal?.type === 'reset' && (
        <ResetPasswordModal
          target={modal.user}
          onSave={handleResetPassword}
          onClose={() => setModal(null)}
        />
      )}

      {modal?.type === 'delete' && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70">
          <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-xl w-full max-w-sm mx-4 p-6">
            <h2 className="text-base font-semibold text-white mb-2">Delete User</h2>
            <p className="text-sm text-gray-300 mb-1">
              Delete <strong>{modal.user.username}</strong> ({modal.user.displayName})?
            </p>
            <p className="text-xs text-gray-500 mb-5">This cannot be undone.</p>
            <div className="flex gap-3">
              <button
                onClick={handleDelete}
                className="flex-1 py-2 rounded bg-red-700 hover:bg-red-600 text-sm font-medium text-white transition-colors"
              >
                Delete
              </button>
              <button
                onClick={() => setModal(null)}
                className="flex-1 py-2 rounded bg-gray-700 hover:bg-gray-600 text-sm font-medium text-white transition-colors"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
