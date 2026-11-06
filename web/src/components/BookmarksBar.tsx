import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api'
import { keys } from '../cache'
import { toast } from '../store'
import { Popover } from './ui'

export function BookmarksBar({ spaceId }: { spaceId: string }) {
  const qc = useQueryClient()
  const q = useQuery({ queryKey: keys.bookmarks(spaceId), queryFn: () => api.bookmarks(spaceId), staleTime: 5 * 60_000 })
  const [adding, setAdding] = useState<HTMLElement | null>(null)
  const [url, setUrl] = useState('')
  const [title, setTitle] = useState('')
  const list = q.data ?? []

  const add = async () => {
    try {
      await api.addBookmark(spaceId, title, url)
      setAdding(null)
      setUrl('')
      setTitle('')
      qc.invalidateQueries({ queryKey: keys.bookmarks(spaceId) })
    } catch {
      toast('Neispravan link.', 'error')
    }
  }

  return (
    <div className="bookmarks">
      {list.map((b) => (
        <span key={b.id} className="bookmark">
          <a href={b.url} target="_blank" rel="noreferrer" title={b.url}>
            🔗 {b.title}
          </a>
          <button
            className="icon tiny"
            aria-label="Ukloni"
            onClick={() => api.removeBookmark(spaceId, b.id).then(() => qc.invalidateQueries({ queryKey: keys.bookmarks(spaceId) }))}
          >
            ✕
          </button>
        </span>
      ))}
      <button className="bookmark add" onClick={(e) => setAdding(e.currentTarget)}>
        + Dodaj link
      </button>
      {adding && (
        <Popover anchor={adding} onClose={() => setAdding(null)}>
          <form
            className="form-pop"
            onSubmit={(e) => {
              e.preventDefault()
              add()
            }}
          >
            <label>
              Link
              <input autoFocus type="url" required placeholder="https://…" value={url} onChange={(e) => setUrl(e.target.value)} />
            </label>
            <label>
              Naziv
              <input placeholder="npr. Jira board" value={title} onChange={(e) => setTitle(e.target.value)} />
            </label>
            <button className="btn primary small">Dodaj</button>
          </form>
        </Popover>
      )}
    </div>
  )
}
