import { useEffect, useState } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import { api, ApiError } from './api/client'
import type { LocalUserDto } from './types'
import { GroupProvider } from './context/GroupContext'
import { JobsProvider } from './context/JobsContext'
import { ConfirmProvider } from './context/ConfirmContext'
import JobsDrawer from './components/JobsDrawer'
import SetupPage from './pages/SetupPage'
import LoginPage from './pages/LoginPage'
import Layout from './components/Layout'
import DashboardPage from './pages/DashboardPage'
import MembersPage from './pages/MembersPage'
import AuditLogPage      from './pages/AuditLogPage'
import BadgeScannerPage  from './pages/BadgeScannerPage'
import KillSwitchPage    from './pages/KillSwitchPage'
import LocalUsersPage    from './pages/LocalUsersPage'
import ServerAccessPage  from './pages/ServerAccessPage'
import SettingsPage from './pages/SettingsPage'

type BootState = 'loading' | 'setup' | 'login' | 'ready'

export default function App() {
  const [boot,    setBoot]    = useState<BootState>('loading')
  const [user,    setUser]    = useState<LocalUserDto | null>(null)

  // Determine initial app state on mount
  useEffect(() => {
    let cancelled = false

    async function init() {
      // 1. Already authenticated?
      try {
        const me = await api.auth.me()
        if (!cancelled) { setUser(me); setBoot('ready') }
        return
      } catch (e) {
        if (e instanceof ApiError && e.status !== 401) {
          // Server unreachable or unexpected error — show login anyway
        }
      }

      // 2. Setup required?
      try {
        const { setupRequired } = await api.setup.status()
        if (!cancelled) setBoot(setupRequired ? 'setup' : 'login')
      } catch {
        if (!cancelled) setBoot('login')
      }
    }

    init()
    return () => { cancelled = true }
  }, [])

  const handleSetupDone = () => setBoot('login')

  const handleLogin = (loggedIn: LocalUserDto) => {
    setUser(loggedIn)
    setBoot('ready')
  }

  const handleLogout = async () => {
    await api.auth.logout().catch(() => {})
    setUser(null)
    setBoot('login')
  }

  // Loading splash
  if (boot === 'loading') {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-950">
        <p className="text-gray-400 text-sm animate-pulse">Connecting to local server…</p>
      </div>
    )
  }

  const defaultRedirect =
    boot === 'ready' ? '/app/dashboard' :
    boot === 'setup' ? '/setup'          : '/login'

  return (
    <Routes>
      <Route path="/setup" element={
        boot === 'setup'
          ? <SetupPage onDone={handleSetupDone} />
          : <Navigate to={defaultRedirect} replace />
      } />

      <Route path="/login" element={
        boot === 'ready'
          ? <Navigate to="/app/dashboard" replace />
          : <LoginPage onLogin={handleLogin} />
      } />

      {/* Protected app shell — nested routes render via <Outlet> in Layout */}
      <Route
        path="/app"
        element={
          user
            ? (
              <ConfirmProvider>
                <GroupProvider>
                  <JobsProvider>
                    <Layout user={user} onLogout={handleLogout} />
                    <JobsDrawer />
                  </JobsProvider>
                </GroupProvider>
              </ConfirmProvider>
            )
            : <Navigate to="/login" replace />
        }
      >
        <Route index element={<Navigate to="dashboard" replace />} />
        <Route path="dashboard" element={<DashboardPage />} />
        <Route path="members"   element={<MembersPage />} />
        <Route path="audit"          element={<AuditLogPage />} />
        <Route path="badge-scanner"  element={<BadgeScannerPage />} />
        <Route path="kill-switch"     element={<KillSwitchPage />} />
        <Route path="local-users"      element={<LocalUsersPage />} />
        <Route path="server-access"    element={<ServerAccessPage />} />
        <Route path="settings"  element={<SettingsPage />} />
      </Route>

      {/* Catch-all */}
      <Route path="*" element={<Navigate to={defaultRedirect} replace />} />
    </Routes>
  )
}
