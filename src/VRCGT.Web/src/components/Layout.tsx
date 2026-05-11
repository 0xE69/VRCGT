import { Outlet } from 'react-router-dom'
import type { LocalUserDto } from '../types'
import Sidebar from './Sidebar'
import TopBar from './TopBar'

interface LayoutProps {
  user: LocalUserDto
  onLogout: () => void
}

export default function Layout({ user, onLogout }: LayoutProps) {
  return (
    <div className="flex h-screen overflow-hidden bg-gray-950 text-gray-100">
      {/* Left sidebar */}
      <Sidebar />

      {/* Right side: top bar + scrollable content */}
      <div className="flex flex-col flex-1 min-w-0">
        <TopBar user={user} onLogout={onLogout} />

        <main className="flex-1 overflow-hidden p-6 flex flex-col">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
