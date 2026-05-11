import {
  createContext,
  useCallback,
  useContext,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import ConfirmDialog, { type ConfirmSeverity } from '../components/ConfirmDialog'
import DangerConfirmDialog from '../components/DangerConfirmDialog'

// ── Options ───────────────────────────────────────────────────────────────────

export interface ConfirmOptions {
  title: string
  message: React.ReactNode
  confirmLabel?: string
  cancelLabel?: string
  severity?: ConfirmSeverity
  /** When set, user must type this exact phrase to enable the confirm button. */
  requiredPhrase?: string
  /**
   * Number of affected items shown in the message.
   * Passed through for convenience; callers typically embed it in `message`.
   */
  affectedCount?: number
}

// ── Context ───────────────────────────────────────────────────────────────────

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

const ConfirmContext = createContext<ConfirmFn | null>(null)

// ── Provider ──────────────────────────────────────────────────────────────────

interface DialogState {
  open: boolean
  options: ConfirmOptions
}

const CLOSED: DialogState = {
  open: false,
  options: { title: '', message: '' },
}

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [dialog, setDialog] = useState<DialogState>(CLOSED)
  // resolveRef holds the resolve function of the current Promise
  const resolveRef = useRef<((value: boolean) => void) | null>(null)

  const confirm = useCallback((options: ConfirmOptions): Promise<boolean> => {
    return new Promise(resolve => {
      resolveRef.current = resolve
      setDialog({ open: true, options })
    })
  }, [])

  const handleConfirm = useCallback(() => {
    setDialog(CLOSED)
    resolveRef.current?.(true)
    resolveRef.current = null
  }, [])

  const handleCancel = useCallback(() => {
    setDialog(CLOSED)
    resolveRef.current?.(false)
    resolveRef.current = null
  }, [])

  const { open, options } = dialog

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}

      {/* Render the appropriate dialog variant */}
      {options.requiredPhrase ? (
        <DangerConfirmDialog
          open={open}
          title={options.title}
          message={options.message}
          requiredPhrase={options.requiredPhrase}
          confirmLabel={options.confirmLabel}
          cancelLabel={options.cancelLabel}
          onConfirm={handleConfirm}
          onCancel={handleCancel}
        />
      ) : (
        <ConfirmDialog
          open={open}
          title={options.title}
          message={options.message}
          confirmLabel={options.confirmLabel}
          cancelLabel={options.cancelLabel}
          severity={options.severity ?? 'warning'}
          onConfirm={handleConfirm}
          onCancel={handleCancel}
        />
      )}
    </ConfirmContext.Provider>
  )
}

// ── Hook ──────────────────────────────────────────────────────────────────────

/**
 * Returns an imperative `confirm(options)` function that shows a modal and
 * resolves to `true` when the user confirms, or `false` when they cancel.
 *
 * Usage:
 * ```ts
 * const confirm = useConfirm()
 * const ok = await confirm({ title: 'Delete?', message: '...', severity: 'danger' })
 * if (!ok) return
 * await doDelete()
 * ```
 */
export function useConfirm(): ConfirmFn {
  const fn = useContext(ConfirmContext)
  if (!fn) throw new Error('useConfirm must be used inside <ConfirmProvider>')
  return fn
}
