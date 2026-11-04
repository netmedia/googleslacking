import { useMemo, useState, type KeyboardEvent } from 'react'
import type { Space } from '../api'

interface Props {
  spaces: Space[]
  onSelect: (id: string) => void
  onClose: () => void
}

const norm = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').toLowerCase()

export function QuickSwitcher({ spaces, onSelect, onClose }: Props) {
  const [query, setQuery] = useState('')
  const [index, setIndex] = useState(0)

  const results = useMemo(() => {
    const q = norm(query.trim())
    const list = q ? spaces.filter((s) => norm(s.displayName).includes(q)) : spaces
    // Unread first, then most recent (spaces already come sorted by activity).
    return [...list].sort((a, b) => Number(b.unreadCount > 0) - Number(a.unreadCount > 0)).slice(0, 12)
  }, [query, spaces])

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Escape') onClose()
    else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setIndex((i) => Math.min(i + 1, results.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setIndex((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter' && results[index]) onSelect(results[index].id)
  }

  return (
    <div className="overlay" onMouseDown={onClose}>
      <div className="switcher" onMouseDown={(e) => e.stopPropagation()}>
        <input
          autoFocus
          placeholder="Idi na razgovor…"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setIndex(0)
          }}
          onKeyDown={onKeyDown}
        />
        <ul>
          {results.map((s, i) => (
            <li key={s.id} className={i === index ? 'selected' : ''} onMouseEnter={() => setIndex(i)} onClick={() => onSelect(s.id)}>
              <span className="glyph">{s.kind === 0 ? '#' : '●'}</span>
              <span className="name">{s.displayName}</span>
              {s.unreadCount > 0 && <span className="badge">{s.unreadCount}</span>}
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
