import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, newClientId, type Me, type Message, type Space } from '../api'
import { markFailed, upsertMessages } from '../cache'

interface Props {
  space: Space
  me: Me
  threadName?: string | null
  placeholder: string
}

const draftKey = (spaceId: string, thread?: string | null) => `draft:${spaceId}:${thread ?? ''}`
const loadDraft = (k: string) => {
  try {
    return localStorage.getItem(k) ?? ''
  } catch {
    return ''
  }
}
const saveDraft = (k: string, v: string) => {
  try {
    if (v) localStorage.setItem(k, v)
    else localStorage.removeItem(k)
  } catch {
    /* storage unavailable */
  }
}

export function Composer({ space, me, threadName, placeholder }: Props) {
  const qc = useQueryClient()
  const key = draftKey(space.id, threadName)
  const [text, setText] = useState(() => loadDraft(key))
  const ref = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    ref.current?.focus()
  }, [space.id, threadName])

  useEffect(() => {
    const t = setTimeout(() => saveDraft(key, text), 300)
    return () => clearTimeout(t)
  }, [key, text])

  // Auto-grow
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = Math.min(el.scrollHeight, 240) + 'px'
  }, [text])

  const send = async () => {
    const body = text.trim()
    if (!body) return
    const clientId = newClientId()
    const optimistic: Message = {
      id: clientId,
      spaceId: space.id,
      googleName: clientId,
      threadName: threadName ?? null,
      isThreadReply: !!threadName,
      sender: { id: me.googleUserName, displayName: me.displayName, avatarUrl: me.avatarUrl, email: me.email },
      text: body,
      formattedText: null,
      reactions: null,
      attachments: null,
      createTime: new Date().toISOString(),
      lastUpdateTime: null,
      isDeleted: false,
      clientAssignedMessageId: clientId,
      pending: true,
    }
    upsertMessages(qc, space.id, [optimistic])
    setText('')
    saveDraft(key, '')

    try {
      const saved = await api.send(space.id, body, clientId, threadName)
      if (saved) upsertMessages(qc, space.id, [saved])
    } catch {
      markFailed(qc, space.id, clientId)
    }
  }

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      send()
    }
  }

  return (
    <div className="composer">
      <textarea
        ref={ref}
        rows={1}
        value={text}
        placeholder={placeholder}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={onKeyDown}
      />
      <button className="btn primary" onClick={send} disabled={!text.trim()}>
        Pošalji
      </button>
    </div>
  )
}
