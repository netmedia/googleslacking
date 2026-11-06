import type { ReactNode } from 'react'
import type { Annotation } from '../api'

const URL_RE = /(https?:\/\/[^\s<>]+[^\s<>.,;:!?)\]'"])/g

export const firstUrl = (text: string | null) => text?.match(URL_RE)?.[0] ?? null

/**
 * Renders Google Chat text: mentions (from annotations), links, *bold*, _italic_, ~strike~, `code`,
 * ```code blocks``` and "> quote" lines.
 */
export function RichText({ text, annotations, me }: { text: string; annotations: Annotation[] | null; me: string }) {
  // Mentions come as annotation ranges over the text.
  const mentions = (annotations ?? [])
    .filter((a) => a.type === 'USER_MENTION' && a.startIndex !== undefined && a.length)
    .sort((a, b) => a.startIndex! - b.startIndex!)

  const pieces: ReactNode[] = []
  let pos = 0
  mentions.forEach((a, i) => {
    const start = a.startIndex!
    const end = start + a.length!
    if (start < pos || end > text.length) return
    if (start > pos) pieces.push(<Blocks key={`t${i}`} text={text.slice(pos, start)} />)
    const user = a.userMention?.user?.name
    const isMe = user === me || user === 'users/all'
    pieces.push(
      <span key={`m${i}`} className={`mention ${isMe ? 'me' : ''}`} title={user}>
        {text.slice(start, end)}
      </span>,
    )
    pos = end
  })
  if (pos < text.length) pieces.push(<Blocks key="rest" text={text.slice(pos)} />)
  return <>{pieces}</>
}

function Blocks({ text }: { text: string }) {
  // Split on code fences first; everything inside is literal.
  const parts = text.split(/```/)
  return (
    <>
      {parts.map((part, i) =>
        i % 2 === 1 ? (
          <pre key={i} className="code-block">
            <code>{part.replace(/^\n/, '')}</code>
          </pre>
        ) : (
          <Lines key={i} text={part} />
        ),
      )}
    </>
  )
}

function Lines({ text }: { text: string }) {
  const lines = text.split('\n')
  const out: ReactNode[] = []
  let quote: string[] = []
  const flushQuote = (key: string) => {
    if (!quote.length) return
    out.push(
      <blockquote key={key}>
        {quote.map((q, j) => (
          <span key={j}>
            {inline(q, `${key}-${j}`)}
            {j < quote.length - 1 && '\n'}
          </span>
        ))}
      </blockquote>,
    )
    quote = []
  }
  lines.forEach((line, i) => {
    if (line.startsWith('> ') || line === '>') {
      quote.push(line.replace(/^> ?/, ''))
      return
    }
    flushQuote(`q${i}`)
    out.push(
      <span key={i}>
        {inline(line, String(i))}
        {i < lines.length - 1 && '\n'}
      </span>,
    )
  })
  flushQuote('qend')
  return <>{out}</>
}

function inline(text: string, keyBase: string): ReactNode[] {
  return text.split(URL_RE).map((part, i) => {
    if (i % 2 === 1)
      return (
        <a key={`${keyBase}-${i}`} href={part} target="_blank" rel="noreferrer">
          {part}
        </a>
      )
    return part.split(/(\*[^*\n]+\*|_[^_\n]+_|~[^~\n]+~|`[^`\n]+`)/g).map((seg, j) => {
      const k = `${keyBase}-${i}-${j}`
      if (/^\*[^*]+\*$/.test(seg)) return <strong key={k}>{seg.slice(1, -1)}</strong>
      if (/^_[^_]+_$/.test(seg)) return <em key={k}>{seg.slice(1, -1)}</em>
      if (/^~[^~]+~$/.test(seg)) return <s key={k}>{seg.slice(1, -1)}</s>
      if (/^`[^`]+`$/.test(seg)) return <code key={k}>{seg.slice(1, -1)}</code>
      return seg
    })
  })
}
