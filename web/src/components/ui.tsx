import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useToasts } from '../store'

/** Popover anchored to an element (or a point); closes on outside click / Esc. */
export function Popover({
  anchor,
  onClose,
  children,
  align = 'left',
}: {
  anchor: HTMLElement | { x: number; y: number }
  onClose: () => void
  children: ReactNode
  align?: 'left' | 'right'
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState<{ top: number; left: number }>({ top: -9999, left: -9999 })

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const r = anchor instanceof HTMLElement ? anchor.getBoundingClientRect() : { left: anchor.x, right: anchor.x, top: anchor.y, bottom: anchor.y }
    const w = el.offsetWidth
    const h = el.offsetHeight
    let left = align === 'right' ? r.right - w : r.left
    let top = r.bottom + 4
    if (top + h > window.innerHeight - 8) top = Math.max(8, r.top - h - 4)
    left = Math.min(Math.max(8, left), window.innerWidth - w - 8)
    setPos({ top, left })
  }, [anchor, align])

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose()
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    setTimeout(() => document.addEventListener('mousedown', onDown))
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [onClose])

  return createPortal(
    <div ref={ref} className="popover" style={{ top: pos.top, left: pos.left }}>
      {children}
    </div>,
    document.body,
  )
}

export interface MenuItem {
  label: string
  onClick?: () => void
  danger?: boolean
  checked?: boolean
  hint?: string
  submenu?: MenuItem[]
  divider?: boolean
}

export function Menu({ items, onClose }: { items: MenuItem[]; onClose: () => void }) {
  const [sub, setSub] = useState<{ items: MenuItem[]; anchor: HTMLElement } | null>(null)
  return (
    <div className="menu" role="menu">
      {items.map((it, i) =>
        it.divider ? (
          <div key={i} className="menu-divider" />
        ) : (
          <button
            key={i}
            role="menuitem"
            className={`menu-item ${it.danger ? 'danger' : ''}`}
            onMouseEnter={(e) => setSub(it.submenu ? { items: it.submenu, anchor: e.currentTarget } : null)}
            onClick={(e) => {
              if (it.submenu) {
                setSub({ items: it.submenu, anchor: e.currentTarget })
                return
              }
              it.onClick?.()
              onClose()
            }}
          >
            <span className="check">{it.checked ? '✓' : ''}</span>
            <span className="label">{it.label}</span>
            {it.hint && <span className="hint">{it.hint}</span>}
            {it.submenu && <span className="hint">›</span>}
          </button>
        ),
      )}
      {sub && (
        <Popover anchor={sub.anchor} onClose={() => setSub(null)} align="left">
          <Menu items={sub.items} onClose={onClose} />
        </Popover>
      )}
    </div>
  )
}

/** Button that opens a menu. */
export function MenuButton({
  items,
  children,
  className,
  title,
  align = 'left',
}: {
  items: MenuItem[] | (() => MenuItem[])
  children: ReactNode
  className?: string
  title?: string
  align?: 'left' | 'right'
}) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  return (
    <>
      <button className={className} title={title} aria-label={title} onClick={(e) => setAnchor(anchor ? null : e.currentTarget)}>
        {children}
      </button>
      {anchor && (
        <Popover anchor={anchor} onClose={() => setAnchor(null)} align={align}>
          <Menu items={typeof items === 'function' ? items() : items} onClose={() => setAnchor(null)} />
        </Popover>
      )}
    </>
  )
}

export function Modal({ title, onClose, children, wide }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])
  return createPortal(
    <div className="overlay" onMouseDown={onClose}>
      <div className={`modal ${wide ? 'wide' : ''}`} onMouseDown={(e) => e.stopPropagation()} role="dialog" aria-label={title}>
        <header className="modal-header">
          <h2>{title}</h2>
          <button className="icon" onClick={onClose} aria-label="Zatvori">
            ✕
          </button>
        </header>
        <div className="modal-body">{children}</div>
      </div>
    </div>,
    document.body,
  )
}

export function Toasts() {
  const list = useToasts()
  return (
    <div className="toasts" aria-live="polite">
      {list.map((t) => (
        <div key={t.id} className={`toast ${t.kind}`}>
          {t.text}
        </div>
      ))}
    </div>
  )
}

/** Small "pick a date/time" popover content used for reminders and scheduling. */
export function WhenPicker({
  presets,
  onPick,
  submitLabel = 'Postavi',
}: {
  presets: { label: string; at: () => Date }[]
  onPick: (d: Date) => void
  submitLabel?: string
}) {
  const [custom, setCustom] = useState('')
  return (
    <div className="menu when-picker">
      {presets.map((p) => (
        <button key={p.label} className="menu-item" onClick={() => onPick(p.at())}>
          <span className="check" />
          <span className="label">{p.label}</span>
          <span className="hint">{p.at().toLocaleString('hr-HR', { weekday: 'short', hour: '2-digit', minute: '2-digit' })}</span>
        </button>
      ))}
      <div className="menu-divider" />
      <form
        className="when-custom"
        onSubmit={(e) => {
          e.preventDefault()
          if (custom) onPick(new Date(custom))
        }}
      >
        <input type="datetime-local" value={custom} onChange={(e) => setCustom(e.target.value)} />
        <button className="btn primary small" disabled={!custom}>
          {submitLabel}
        </button>
      </form>
    </div>
  )
}
