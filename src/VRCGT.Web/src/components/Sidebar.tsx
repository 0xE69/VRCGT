import { NavLink } from 'react-router-dom'

const navItems = [
  { to: '/app/dashboard',    label: 'Dashboard',      icon: '⬛' },
  { to: '/app/members',      label: 'Members',        icon: '👥' },
  { to: '/app/audit',        label: 'Audit Log',      icon: '📋' },
  { to: '/app/badge-scanner', label: 'Badge Scanner', icon: '🔍' },
  { to: '/app/kill-switch',  label: 'Kill Switch',    icon: '⚡' },
  { to: '/app/local-users',  label: 'Local Users',    icon: '👤' },
  { to: '/app/server-access', label: 'Server Access', icon: '🌐' },
  { to: '/app/settings',     label: 'Settings',       icon: '⚙' },
]

export default function Sidebar() {
  return (
    <aside className="w-56 shrink-0 flex flex-col bg-gray-900 border-r border-gray-800">
      {/* Brand */}
      <div className="h-14 flex items-center px-4 border-b border-gray-800">
        <span className="font-bold text-sm tracking-wide text-white">VRCGT</span>
        <span className="ml-2 text-xs text-gray-500">v2 alpha</span>
      </div>

      {/* Nav */}
      <nav className="flex-1 py-3 px-2 flex flex-col gap-1">
        {navItems.map(({ to, label, icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              `flex items-center gap-2 px-3 py-2 rounded-md text-sm transition-colors ` +
              (isActive
                ? 'bg-blue-600/20 text-blue-400 font-medium'
                : 'text-gray-400 hover:bg-gray-800 hover:text-gray-100')
            }
          >
            <span className="text-base leading-none">{icon}</span>
            {label}
          </NavLink>
        ))}
      </nav>

      {/* Footer */}
      <div className="p-3 border-t border-gray-800 text-xs text-gray-600 text-center">
        Local only · 127.0.0.1
      </div>
    </aside>
  )
}
