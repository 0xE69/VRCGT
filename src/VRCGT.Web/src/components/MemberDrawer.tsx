import type { GroupMemberDto } from '../types'

interface Props {
  member: GroupMemberDto | null
  onClose: () => void
}

function fmtDate(iso: string | null | undefined) {
  if (!iso) return '—'
  try { return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) }
  catch { return iso }
}

export default function MemberDrawer({ member, onClose }: Props) {
  if (!member) return null

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/40 z-40"
        onClick={onClose}
      />

      {/* Panel */}
      <aside className="fixed right-0 top-0 h-full w-80 bg-gray-900 border-l border-gray-800 z-50 flex flex-col overflow-y-auto">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-gray-800 shrink-0">
          <h2 className="text-sm font-semibold text-white">Member detail</h2>
          <button
            onClick={onClose}
            className="text-gray-500 hover:text-gray-200 text-lg leading-none px-1"
            aria-label="Close"
          >
            ×
          </button>
        </div>

        {/* Body */}
        <div className="flex flex-col gap-5 p-5">
          {/* Avatar + name */}
          <div className="flex items-center gap-3">
            {member.avatarUrl ? (
              <img
                src={member.avatarUrl}
                alt={member.displayName}
                className="w-12 h-12 rounded-full object-cover bg-gray-800 shrink-0"
              />
            ) : (
              <div className="w-12 h-12 rounded-full bg-gray-800 flex items-center justify-center text-xl shrink-0">
                👤
              </div>
            )}
            <div className="min-w-0">
              <p className="text-white font-semibold truncate">{member.displayName}</p>
              {member.isRepresenting && (
                <span className="text-xs text-violet-400">Representing</span>
              )}
            </div>
          </div>

          {/* Fields */}
          <dl className="flex flex-col gap-3 text-sm">
            <Field label="User ID"  value={member.userId} mono />
            <Field label="Group ID" value={member.groupId} mono />
            <Field label="Joined"   value={fmtDate(member.joinedAt)} />
            <Field label="Cached"   value={fmtDate(member.cachedAt)} />
          </dl>

          {/* Roles */}
          {member.roleNames.length > 0 && (
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wider mb-2">Roles</p>
              <div className="flex flex-wrap gap-1.5">
                {member.roleNames.map((name, i) => (
                  <span
                    key={member.roleIds[i] ?? i}
                    title={member.roleIds[i]}
                    className="text-xs bg-violet-600/20 border border-violet-500/30 text-violet-300 rounded-full px-2 py-0.5"
                  >
                    {name}
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>
      </aside>
    </>
  )
}

function Field({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <dt className="text-xs text-gray-500 uppercase tracking-wider mb-0.5">{label}</dt>
      <dd className={`text-gray-200 break-all ${mono ? 'font-mono text-xs' : 'text-sm'}`}>{value}</dd>
    </div>
  )
}
