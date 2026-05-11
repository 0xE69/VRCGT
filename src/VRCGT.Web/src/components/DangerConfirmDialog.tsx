import { useEffect, useRef, useState } from 'react'
import ConfirmDialog from './ConfirmDialog'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface DangerConfirmDialogProps {
  open: boolean
  title: string
  message: React.ReactNode
  /** The exact phrase the user must type to unlock the confirm button. */
  requiredPhrase: string
  confirmLabel?: string
  cancelLabel?: string
  onConfirm: () => void
  onCancel: () => void
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function DangerConfirmDialog({
  open,
  title,
  message,
  requiredPhrase,
  confirmLabel = 'Confirm',
  cancelLabel  = 'Cancel',
  onConfirm,
  onCancel,
}: DangerConfirmDialogProps) {
  const [typed,   setTyped]   = useState('')
  const inputRef = useRef<HTMLInputElement>(null)
  const matched  = typed === requiredPhrase

  // Reset typed phrase each time dialog opens
  useEffect(() => {
    if (open) {
      setTyped('')
      // Focus the input shortly after the panel mounts (after ConfirmDialog's
      // own focus-management rAF fires for the cancel button)
      const t = setTimeout(() => inputRef.current?.focus(), 60)
      return () => clearTimeout(t)
    }
  }, [open])

  return (
    <ConfirmDialog
      open={open}
      title={title}
      message={message}
      confirmLabel={confirmLabel}
      cancelLabel={cancelLabel}
      severity="danger"
      onConfirm={onConfirm}
      onCancel={onCancel}
      confirmDisabled={!matched}
    >
      {/* Typed confirmation section */}
      <div className="flex flex-col gap-2">
        <label className="text-xs text-zinc-400">
          Type{' '}
          <code className="font-mono bg-zinc-800 text-red-300 px-1 rounded">
            {requiredPhrase}
          </code>{' '}
          to confirm:
        </label>
        <input
          ref={inputRef}
          type="text"
          value={typed}
          onChange={e => setTyped(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter' && matched) onConfirm() }}
          autoComplete="off"
          spellCheck={false}
          placeholder={requiredPhrase}
          className={`text-sm rounded-lg px-3 py-2 border bg-zinc-800 text-zinc-100 placeholder-zinc-600 focus:outline-none focus:ring-2 transition-colors ${
            matched
              ? 'border-red-500 focus:ring-red-500'
              : 'border-zinc-700 focus:ring-zinc-500'
          }`}
        />
        {typed.length > 0 && !matched && (
          <p className="text-xs text-red-400">
            Phrase does not match.
          </p>
        )}
      </div>
    </ConfirmDialog>
  )
}
