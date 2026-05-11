import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'

// ── Types ─────────────────────────────────────────────────────────────────────

export type ConfirmSeverity = 'info' | 'warning' | 'danger'

export interface ConfirmDialogProps {
  open: boolean
  title: string
  message: React.ReactNode
  confirmLabel?: string
  cancelLabel?: string
  severity?: ConfirmSeverity
  onConfirm: () => void
  onCancel: () => void
  /** When true, confirm button is disabled (used by DangerConfirmDialog) */
  confirmDisabled?: boolean
  /** Extra content rendered between the message and the action buttons */
  children?: React.ReactNode
}

// ── Severity palette ──────────────────────────────────────────────────────────

const severityStyles: Record<ConfirmSeverity, {
  iconBg: string; icon: string; confirmBtn: string; titleColor: string
}> = {
  info: {
    iconBg:     'bg-blue-900/50',
    icon:       'ℹ',
    titleColor: 'text-white',
    confirmBtn: 'bg-blue-600 hover:bg-blue-500 focus-visible:ring-blue-500',
  },
  warning: {
    iconBg:     'bg-yellow-900/50',
    icon:       '⚠',
    titleColor: 'text-yellow-100',
    confirmBtn: 'bg-yellow-600 hover:bg-yellow-500 focus-visible:ring-yellow-500',
  },
  danger: {
    iconBg:     'bg-red-900/50',
    icon:       '⚠',
    titleColor: 'text-red-100',
    confirmBtn: 'bg-red-600 hover:bg-red-500 focus-visible:ring-red-500',
  },
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel  = 'Cancel',
  severity     = 'warning',
  onConfirm,
  onCancel,
  confirmDisabled = false,
  children,
}: ConfirmDialogProps) {
  const panelRef  = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)
  const style     = severityStyles[severity]

  // ── Focus management ───────────────────────────────────────────────────────

  // Move focus into the dialog when it opens; restore it when it closes.
  useEffect(() => {
    if (!open) return
    const previous = document.activeElement as HTMLElement | null
    // Small rAF to let the portal mount before we try to focus
    const raf = requestAnimationFrame(() => cancelRef.current?.focus())
    return () => {
      cancelAnimationFrame(raf)
      previous?.focus()
    }
  }, [open])

  // Focus trap: keep Tab / Shift-Tab cycling inside the dialog.
  useEffect(() => {
    if (!open) return
    const panel = panelRef.current
    if (!panel) return

    const onKeyDown = (e: KeyboardEvent) => {
      // Escape: close only when severity is not 'danger'
      if (e.key === 'Escape' && severity !== 'danger') {
        e.preventDefault()
        onCancel()
        return
      }

      if (e.key !== 'Tab') return

      const focusable = panel.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])'
      )
      if (focusable.length === 0) return

      const first = focusable[0]
      const last  = focusable[focusable.length - 1]

      if (e.shiftKey) {
        if (document.activeElement === first) {
          e.preventDefault()
          last.focus()
        }
      } else {
        if (document.activeElement === last) {
          e.preventDefault()
          first.focus()
        }
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [open, severity, onCancel])

  if (!open) return null

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-dialog-title"
      aria-describedby="confirm-dialog-desc"
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
    >
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/60 backdrop-blur-sm"
        aria-hidden="true"
        onClick={severity !== 'danger' ? onCancel : undefined}
      />

      {/* Panel */}
      <div
        ref={panelRef}
        className="relative z-10 w-full max-w-md bg-zinc-900 border border-zinc-700 rounded-2xl shadow-2xl p-6 flex flex-col gap-4"
      >
        {/* Header row */}
        <div className="flex items-start gap-4">
          <div className={`w-10 h-10 rounded-full flex items-center justify-center text-lg shrink-0 ${style.iconBg}`}>
            {style.icon}
          </div>
          <div className="flex-1 min-w-0">
            <h2
              id="confirm-dialog-title"
              className={`text-base font-semibold ${style.titleColor}`}
            >
              {title}
            </h2>
            <div
              id="confirm-dialog-desc"
              className="text-sm text-zinc-400 mt-1 leading-relaxed"
            >
              {message}
            </div>
          </div>
        </div>

        {/* Extra content (e.g. typed confirmation input) */}
        {children && <div>{children}</div>}

        {/* Action buttons — Cancel is leftmost and default-focused */}
        <div className="flex justify-end gap-3 pt-1">
          <button
            ref={cancelRef}
            onClick={onCancel}
            className="px-4 py-2 text-sm rounded-lg bg-zinc-700 hover:bg-zinc-600 text-zinc-200 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-zinc-400"
          >
            {cancelLabel}
          </button>
          <button
            onClick={onConfirm}
            disabled={confirmDisabled}
            className={`px-4 py-2 text-sm rounded-lg text-white font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 disabled:opacity-40 disabled:cursor-not-allowed ${style.confirmBtn}`}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}
