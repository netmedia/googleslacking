import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { keys } from '../cache'

export function LinkPreviewCard({ url }: { url: string }) {
  const q = useQuery({
    queryKey: keys.unfurl(url),
    queryFn: () => api.unfurl(url).then((p) => p ?? null),
    staleTime: 24 * 60 * 60_000,
    gcTime: 24 * 60 * 60_000,
    retry: false,
  })
  const p = q.data
  if (!p || (!p.title && !p.description)) return null
  return (
    <a className="link-preview" href={url} target="_blank" rel="noreferrer">
      <div className="lp-text">
        {p.siteName && <span className="lp-site">{p.siteName}</span>}
        {p.title && <strong>{p.title}</strong>}
        {p.description && <span className="lp-desc">{p.description}</span>}
      </div>
      {p.imageUrl && <img src={p.imageUrl} alt="" loading="lazy" referrerPolicy="no-referrer" />}
    </a>
  )
}
