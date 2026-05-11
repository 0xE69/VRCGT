import { useState, useEffect, useCallback } from 'react'
import { api, ApiError } from '../api/client'
import type { AccessSettingsDto } from '../types'

// ── Small helpers ─────────────────────────────────────────────────────────────

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false)
  const handleCopy = () => {
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }
  return (
    <button
      onClick={handleCopy}
      className="ml-2 text-xs px-2 py-0.5 rounded bg-zinc-700 hover:bg-zinc-600 text-zinc-300 transition-colors"
    >
      {copied ? 'Copied!' : 'Copy'}
    </button>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function ServerAccessPage() {
  const [settings, setSettings]     = useState<AccessSettingsDto | null>(null)
  const [loading, setLoading]       = useState(true)
  const [saving, setSaving]         = useState(false)
  const [error, setError]           = useState<string | null>(null)

  // LAN enable flow state
  const [showLanForm, setShowLanForm]         = useState(false)
  const [confirmPhrase, setConfirmPhrase]     = useState('')
  const REQUIRED_PHRASE = 'ENABLE LAN ACCESS'

  const load = useCallback(async () => {
    try {
      const data = await api.systemAccess.get()
      setSettings(data)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to load access settings.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load() }, [load])

  const handleDisable = async () => {
    setSaving(true)
    setError(null)
    try {
      // No confirmation required to disable LAN mode — switching back to safe default
      const updated = await api.systemAccess.update('LocalOnly')
      setSettings(updated)
      setShowLanForm(false)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to update access settings.')
    } finally {
      setSaving(false)
    }
  }

  const handleEnable = async () => {
    if (confirmPhrase.trim() !== REQUIRED_PHRASE) return
    setSaving(true)
    setError(null)
    try {
      // Build the confirmation token the server expects: "ENABLE_LAN_ACCESS:lan"
      const updated = await api.systemAccess.update('Lan', 'ENABLE_LAN_ACCESS:lan')
      setSettings(updated)
      setShowLanForm(false)
      setConfirmPhrase('')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to update access settings.')
    } finally {
      setSaving(false)
    }
  }

  // ── Loading / error states ─────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="p-6 text-zinc-400 animate-pulse">Loading server access settings…</div>
    )
  }

  if (!settings) {
    return (
      <div className="p-6 text-red-400">
        {error ?? 'Could not load settings.'}
      </div>
    )
  }

  const isLanRuntime = settings.runtimeBindMode === 'Lan'
  const isLanSaved   = settings.savedBindMode   === 'Lan'

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="p-6 max-w-2xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-white">Server Access</h1>
        <p className="text-sm text-zinc-400 mt-1">
          Control whether the VRCGT local server is accessible only from this machine
          (default) or from other devices on your local network.
        </p>
      </div>

      {/* ── Current status ─────────────────────────────────────────────────── */}
      <div className="rounded-lg border border-zinc-700 bg-zinc-800 p-4 space-y-3">
        <div className="flex items-center gap-3">
          <span className="text-sm font-medium text-zinc-300 w-40">Runtime mode</span>
          {isLanRuntime ? (
            <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-amber-400">
              <span className="h-2 w-2 rounded-full bg-amber-400"></span>
              LAN Enabled
            </span>
          ) : (
            <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-green-400">
              <span className="h-2 w-2 rounded-full bg-green-400"></span>
              Local Only
            </span>
          )}
        </div>

        <div className="flex items-center gap-3">
          <span className="text-sm font-medium text-zinc-300 w-40">Saved mode</span>
          <span className="text-sm text-zinc-400">
            {isLanSaved ? 'LAN Enabled' : 'Local Only'}
          </span>
        </div>

        <div className="flex items-center gap-3">
          <span className="text-sm font-medium text-zinc-300 w-40">Port</span>
          <span className="text-sm text-zinc-400">{settings.port}</span>
        </div>

        {settings.requiresRestart && (
          <div className="rounded-md bg-amber-900/40 border border-amber-700 text-amber-300 text-sm p-3 mt-2">
            ⚠ A <strong>server restart</strong> is required for the saved mode change to take effect.
          </div>
        )}

        {/* LAN URLs — shown only when LAN mode is live */}
        {isLanRuntime && settings.lanAddresses.length > 0 && (
          <div className="mt-2 space-y-2">
            <p className="text-sm font-medium text-zinc-300">Network addresses</p>
            <ul className="space-y-1">
              {settings.lanAddresses.map(addr => {
                const url = `http://${addr}:${settings.port}`
                return (
                  <li key={addr} className="flex items-center text-sm text-zinc-300 font-mono">
                    {url}
                    <CopyButton text={url} />
                  </li>
                )
              })}
            </ul>
            <p className="text-xs text-zinc-500">
              These URLs are only visible to devices on your local network.
            </p>
          </div>
        )}
      </div>

      {/* ── Warning banner ─────────────────────────────────────────────────── */}
      {(isLanRuntime || isLanSaved) && (
        <div className="rounded-lg border border-amber-700 bg-amber-900/30 p-4 text-amber-300 text-sm space-y-1">
          <p className="font-semibold">⚠ LAN access is enabled</p>
          <p>
            LAN access allows other devices on your network to access VRCGT.
            Anyone on the same Wi-Fi or Ethernet network could reach your group management tools.
            Disable LAN access when it is not needed.
          </p>
        </div>
      )}

      {/* ── Actions ────────────────────────────────────────────────────────── */}
      {error && (
        <div className="rounded-md border border-red-700 bg-red-900/30 text-red-300 text-sm p-3">
          {error}
        </div>
      )}

      {!isLanSaved && !showLanForm && (
        <button
          onClick={() => setShowLanForm(true)}
          className="px-4 py-2 rounded bg-amber-600 hover:bg-amber-500 text-white text-sm font-medium transition-colors"
        >
          Enable LAN access…
        </button>
      )}

      {/* ── LAN enable confirmation form ──────────────────────────────────── */}
      {showLanForm && !isLanSaved && (
        <div className="rounded-lg border border-amber-700 bg-zinc-900 p-5 space-y-4">
          <p className="text-amber-300 font-semibold text-sm">
            ⚠ You are about to enable LAN access
          </p>
          <p className="text-zinc-300 text-sm">
            LAN access allows other devices on your network to access VRCGT.
            This setting cannot be enabled accidentally.
            To confirm, type <code className="bg-zinc-700 rounded px-1 py-0.5 text-amber-300 font-mono">ENABLE LAN ACCESS</code> below.
          </p>
          <input
            type="text"
            value={confirmPhrase}
            onChange={e => setConfirmPhrase(e.target.value)}
            placeholder="ENABLE LAN ACCESS"
            className="w-full rounded bg-zinc-800 border border-zinc-600 text-zinc-100 px-3 py-2 text-sm font-mono focus:outline-none focus:ring-1 focus:ring-amber-500"
          />
          <div className="flex gap-3">
            <button
              onClick={handleEnable}
              disabled={saving || confirmPhrase.trim() !== REQUIRED_PHRASE}
              className="px-4 py-2 rounded bg-amber-600 hover:bg-amber-500 disabled:opacity-40 disabled:cursor-not-allowed text-white text-sm font-medium transition-colors"
            >
              {saving ? 'Saving…' : 'Enable LAN Access'}
            </button>
            <button
              onClick={() => { setShowLanForm(false); setConfirmPhrase('') }}
              disabled={saving}
              className="px-4 py-2 rounded bg-zinc-700 hover:bg-zinc-600 text-zinc-300 text-sm font-medium transition-colors"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {isLanSaved && (
        <button
          onClick={handleDisable}
          disabled={saving}
          className="px-4 py-2 rounded bg-zinc-700 hover:bg-zinc-600 disabled:opacity-40 text-zinc-300 text-sm font-medium transition-colors"
        >
          {saving ? 'Saving…' : 'Disable LAN Access'}
        </button>
      )}
    </div>
  )
}
