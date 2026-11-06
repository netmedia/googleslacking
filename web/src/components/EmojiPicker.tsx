import { useMemo, useState } from 'react'
import { ALL_EMOJI, EMOJI_GROUPS } from '../emoji'
import { storage } from '../store'

const RECENT_KEY = 'emoji:recent'

export function recordRecentEmoji(e: string) {
  const recent = storage.get<string[]>(RECENT_KEY, [])
  storage.set(RECENT_KEY, [e, ...recent.filter((x) => x !== e)].slice(0, 16))
}

export function EmojiPicker({ onPick }: { onPick: (emoji: string) => void }) {
  const [q, setQ] = useState('')
  const recent = storage.get<string[]>(RECENT_KEY, [])
  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase()
    return s ? ALL_EMOJI.filter((x) => x.k.includes(s) || x.e === s) : null
  }, [q])

  const pick = (e: string) => {
    recordRecentEmoji(e)
    onPick(e)
  }

  return (
    <div className="emoji-picker">
      <input autoFocus placeholder="Traži emoji…" value={q} onChange={(e) => setQ(e.target.value)} />
      <div className="emoji-scroll">
        {filtered ? (
          <div className="emoji-grid">
            {filtered.map((x) => (
              <button key={x.e} title={x.k} onClick={() => pick(x.e)}>
                {x.e}
              </button>
            ))}
            {!filtered.length && <span className="muted">Nema rezultata</span>}
          </div>
        ) : (
          <>
            {recent.length > 0 && (
              <>
                <h4>Nedavno</h4>
                <div className="emoji-grid">
                  {recent.map((e) => (
                    <button key={e} onClick={() => pick(e)}>
                      {e}
                    </button>
                  ))}
                </div>
              </>
            )}
            {EMOJI_GROUPS.map((g) => (
              <div key={g.name}>
                <h4>{g.name}</h4>
                <div className="emoji-grid">
                  {g.items.map((x) => (
                    <button key={x.e} title={x.k} onClick={() => pick(x.e)}>
                      {x.e}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </>
        )}
      </div>
    </div>
  )
}
