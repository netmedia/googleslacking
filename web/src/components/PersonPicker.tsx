import { useEffect, useState } from 'react'
import { api, type Member } from '../api'
import { Avatar } from './Avatar'

/** Directory search with multi-select chips. */
export function PersonPicker({
  selected,
  onChange,
  exclude = [],
  autoFocus = true,
}: {
  selected: Member[]
  onChange: (m: Member[]) => void
  exclude?: string[]
  autoFocus?: boolean
}) {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<Member[]>([])
  const [index, setIndex] = useState(0)

  useEffect(() => {
    if (q.trim().length < 2) {
      setResults([])
      return
    }
    let cancelled = false
    const t = setTimeout(() => {
      api.searchPeople(q.trim()).then((r) => {
        if (!cancelled) {
          setResults(r.filter((m) => !exclude.includes(m.id) && !selected.some((s) => s.id === m.id)))
          setIndex(0)
        }
      }, () => {})
    }, 250)
    return () => {
      cancelled = true
      clearTimeout(t)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q, selected.length])

  const pick = (m: Member) => {
    onChange([...selected, m])
    setQ('')
    setResults([])
  }

  return (
    <div className="person-picker">
      <div className="chips">
        {selected.map((s) => (
          <span key={s.id} className="chip">
            {s.displayName ?? s.email}
            <button className="icon tiny" onClick={() => onChange(selected.filter((x) => x.id !== s.id))} aria-label="Ukloni">
              ✕
            </button>
          </span>
        ))}
        <input
          autoFocus={autoFocus}
          placeholder={selected.length ? '' : 'Upiši ime ili e-mail…'}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'ArrowDown') setIndex((i) => Math.min(i + 1, results.length - 1))
            else if (e.key === 'ArrowUp') setIndex((i) => Math.max(i - 1, 0))
            else if (e.key === 'Enter' && results[index]) {
              e.preventDefault()
              pick(results[index])
            } else if (e.key === 'Backspace' && !q && selected.length) onChange(selected.slice(0, -1))
          }}
        />
      </div>
      {results.length > 0 && (
        <ul className="person-results">
          {results.map((m, i) => (
            <li key={m.id} className={i === index ? 'selected' : ''} onMouseEnter={() => setIndex(i)} onMouseDown={(e) => (e.preventDefault(), pick(m))}>
              <Avatar name={m.displayName ?? m.email ?? '?'} url={m.avatarUrl} size={24} />
              <span>{m.displayName}</span>
              <span className="muted">{m.email}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
