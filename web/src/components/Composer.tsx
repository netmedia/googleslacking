import { useEffect, useMemo, useRef, useState, type ClipboardEvent, type DragEvent, type KeyboardEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, newClientId, uploadAttachment, type Me, type Member, type Message, type Space } from '../api'
import { keys, markFailed, upsertMessages } from '../cache'
import { useMembers, useSettings } from '../hooks'
import { resetTyping, sendTyping } from '../realtime'
import { typingText, useTyping } from '../typing'
import { composerBus, storage, toast } from '../store'
import { fmtWhen, schedulePresets } from '../time'
import { Avatar } from './Avatar'
import { EmojiPicker } from './EmojiPicker'
import { Popover, WhenPicker } from './ui'

interface Props {
  space: Space
  me: Me
  threadName?: string | null
  placeholder: string
  /** ↑ in an empty composer: edit my last message. */
  onEditLast?: () => void
}

interface Upload {
  id: string
  file: File
  progress: number
  ref?: unknown
  error?: boolean
}

interface LocalDraft {
  text: string
  at: number
}

const ALL: Member = { id: 'users/all', displayName: 'svi', avatarUrl: null, email: 'Obavijesti sve u razgovoru', isAppUser: false }

export function Composer({ space, me, threadName, placeholder, onEditLast }: Props) {
  const qc = useQueryClient()
  const draftKey = `draft:${space.id}:${threadName ?? ''}`
  const serverDrafts = useQuery({ queryKey: keys.drafts, queryFn: api.drafts, staleTime: Infinity })
  const [text, setText] = useState(() => storage.get<LocalDraft | null>(draftKey, null)?.text ?? '')
  const [uploads, setUploads] = useState<Upload[]>([])
  const [mention, setMention] = useState<{ query: string; start: number; index: number } | null>(null)
  const [pop, setPop] = useState<{ kind: 'emoji' | 'schedule'; anchor: HTMLElement } | null>(null)
  const [dragOver, setDragOver] = useState(false)
  const ref = useRef<HTMLTextAreaElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const mentionMap = useRef(new Map<string, string>()) // "@Display Name" -> "users/123"
  const lastSavedServer = useRef<string | null>(null)
  const members = useMembers(space.id).data
  const hideTyping = useSettings().data?.hideTyping ?? false
  const typingUsers = useTyping(space.id, threadName)
  const typingNames = typingUsers.map((u) => {
    const m = members?.find((x) => x.id === u)
    return m?.displayName?.split(' ')[0] ?? m?.email ?? 'Netko'
  })

  // Take the server draft if it's newer than the local one (written on another device).
  const serverDraft = serverDrafts.data?.find((d) => d.spaceId === space.id && (d.threadName ?? null) === (threadName ?? null))
  useEffect(() => {
    if (!serverDraft) return
    const local = storage.get<LocalDraft | null>(draftKey, null)
    if (!local || Date.parse(serverDraft.updatedAt) > local.at + 1000) {
      if (document.activeElement !== ref.current || !text) setText(serverDraft.text)
    }
    lastSavedServer.current = serverDraft.text
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serverDraft?.updatedAt])

  useEffect(() => {
    ref.current?.focus()
  }, [space.id, threadName])

  // Persist drafts: locally fast, to the server a bit later.
  useEffect(() => {
    const t1 = setTimeout(() => storage.set(draftKey, text ? { text, at: Date.now() } : null), 300)
    const t2 = setTimeout(() => {
      if ((lastSavedServer.current ?? '') === text) return
      lastSavedServer.current = text
      api.saveDraft(space.id, threadName, text).catch(() => {})
    }, 1500)
    return () => {
      clearTimeout(t1)
      clearTimeout(t2)
    }
  }, [draftKey, text, space.id, threadName])

  // Auto-grow
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = Math.min(el.scrollHeight, 300) + 'px'
  }, [text])

  // Quote / insert from elsewhere.
  useEffect(
    () =>
      composerBus.on((e) => {
        if (e.type !== 'insert' || e.spaceId !== space.id || (e.threadName ?? null) !== (threadName ?? null)) return
        setText((t) => (t ? t.replace(/\s*$/, '\n') : '') + e.text)
        setTimeout(() => ref.current?.focus())
      }),
    [space.id, threadName],
  )

  const mentionResults = useMemo(() => {
    if (!mention) return []
    const q = mention.query.toLowerCase()
    const list = (members ?? []).filter((m) => m.id !== me.googleUserName)
    const matches = list.filter((m) => (m.displayName ?? m.email ?? '').toLowerCase().includes(q))
    return [...(space.kind !== 2 && 'svi'.startsWith(q) ? [ALL] : []), ...matches].slice(0, 8)
  }, [mention, members, me.googleUserName, space.kind])

  const uploading = uploads.some((u) => !u.ref && !u.error)

  const addFiles = (files: FileList | File[]) => {
    for (const file of Array.from(files)) {
      if (file.size > 200 * 1024 * 1024) {
        toast(`${file.name} je veći od 200 MB.`, 'error')
        continue
      }
      const id = crypto.randomUUID()
      setUploads((u) => [...u, { id, file, progress: 0 }])
      uploadAttachment(space.id, file, (p) => setUploads((u) => u.map((x) => (x.id === id ? { ...x, progress: p } : x))))
        .then((r) => setUploads((u) => u.map((x) => (x.id === id ? { ...x, ref: r, progress: 1 } : x))))
        .catch(() => setUploads((u) => u.map((x) => (x.id === id ? { ...x, error: true } : x))))
    }
  }

  /** "@Ana Anić" -> "<users/123>" for chosen mentions. */
  const toWire = (t: string) => {
    let out = t
    for (const [display, user] of mentionMap.current) out = out.split(display).join(`<${user}>`)
    return out
  }

  const send = async () => {
    const body = text.trim()
    const ready = uploads.filter((u) => u.ref).map((u) => u.ref)
    if ((!body && !ready.length) || uploading) return
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
      reactions: [],
      attachments: null,
      annotations: null,
      createTime: new Date().toISOString(),
      lastUpdateTime: null,
      isDeleted: false,
      isPinned: false,
      clientAssignedMessageId: clientId,
      pending: true,
    }
    upsertMessages(qc, space.id, [optimistic])
    resetTyping()
    const wire = toWire(body)
    setText('')
    setUploads([])
    mentionMap.current.clear()
    storage.set(draftKey, null)
    lastSavedServer.current = ''
    api.saveDraft(space.id, threadName, '').catch(() => {})

    try {
      const saved = await api.send(space.id, wire, clientId, threadName, ready.length ? ready : undefined)
      if (saved) upsertMessages(qc, space.id, [saved])
    } catch {
      markFailed(qc, space.id, clientId)
      setText((t) => t || body) // give the text back so it isn't lost
    }
  }

  const schedule = async (at: Date) => {
    setPop(null)
    const body = toWire(text.trim())
    if (!body) return
    try {
      await api.schedule(space.id, body, at.toISOString(), threadName)
      setText('')
      mentionMap.current.clear()
      storage.set(draftKey, null)
      qc.invalidateQueries({ queryKey: keys.scheduled })
      toast(`Poruka zakazana ${fmtWhen(at.toISOString())}.`)
    } catch {
      toast('Zakazivanje nije uspjelo.', 'error')
    }
  }

  const pickMention = (m: Member) => {
    if (!mention) return
    const display = `@${m.displayName ?? m.email ?? 'netko'}`
    mentionMap.current.set(display, m.id)
    const caret = ref.current?.selectionStart ?? text.length
    const next = text.slice(0, mention.start) + display + ' ' + text.slice(caret)
    setText(next)
    setMention(null)
    const pos = mention.start + display.length + 1
    setTimeout(() => {
      ref.current?.focus()
      ref.current?.setSelectionRange(pos, pos)
    })
  }

  const updateMention = (value: string, caret: number) => {
    const before = value.slice(0, caret)
    const match = before.match(/(?:^|\s)@([\p{L}\p{N}._-]{0,30})$/u)
    setMention(match ? { query: match[1], start: caret - match[1].length - 1, index: 0 } : null)
  }

  const wrap = (left: string, right = left) => {
    const el = ref.current
    if (!el) return
    const { selectionStart: a, selectionEnd: b } = el
    const selected = text.slice(a, b) || (left === '```' ? 'kod' : 'tekst')
    const next = text.slice(0, a) + left + selected + right + text.slice(b)
    setText(next)
    setTimeout(() => {
      el.focus()
      el.setSelectionRange(a + left.length, a + left.length + selected.length)
    })
  }

  const prefixLines = (prefix: string) => {
    const el = ref.current
    if (!el) return
    const { selectionStart: a, selectionEnd: b } = el
    const start = text.lastIndexOf('\n', a - 1) + 1
    const block = text.slice(start, b).split('\n').map((l) => prefix + l).join('\n')
    setText(text.slice(0, start) + block + text.slice(b))
    setTimeout(() => el.focus())
  }

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (mention && mentionResults.length) {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault()
        const d = e.key === 'ArrowDown' ? 1 : -1
        setMention({ ...mention, index: (mention.index + d + mentionResults.length) % mentionResults.length })
        return
      }
      if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        pickMention(mentionResults[mention.index])
        return
      }
      if (e.key === 'Escape') {
        setMention(null)
        return
      }
    }
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      send()
      return
    }
    if (e.key === 'ArrowUp' && !text && onEditLast) {
      e.preventDefault()
      onEditLast()
      return
    }
    if (e.ctrlKey || e.metaKey) {
      const marker = ({ b: '*', i: '_' } as Record<string, string>)[e.key.toLowerCase()] ?? (e.shiftKey && e.key.toLowerCase() === 'x' ? '~' : null)
      if (marker) {
        e.preventDefault()
        wrap(marker)
      }
    }
  }

  const onPaste = (e: ClipboardEvent<HTMLTextAreaElement>) => {
    if (e.clipboardData.files.length) {
      e.preventDefault()
      addFiles(e.clipboardData.files)
    }
  }

  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    setDragOver(false)
    if (e.dataTransfer.files.length) addFiles(e.dataTransfer.files)
  }

  return (
    <div
      className={`composer ${dragOver ? 'drag-over' : ''}`}
      onDragOver={(e) => {
        if (e.dataTransfer.types.includes('Files')) {
          e.preventDefault()
          setDragOver(true)
        }
      }}
      onDragLeave={() => setDragOver(false)}
      onDrop={onDrop}
    >
      <div className="composer-toolbar">
        <button title="Podebljano (Ctrl+B)" onClick={() => wrap('*')}>
          <b>B</b>
        </button>
        <button title="Kurziv (Ctrl+I)" onClick={() => wrap('_')}>
          <i>I</i>
        </button>
        <button title="Precrtano (Ctrl+Shift+X)" onClick={() => wrap('~')}>
          <s>S</s>
        </button>
        <button title="Kod" onClick={() => wrap('`')}>
          {'</>'}
        </button>
        <button title="Blok koda" onClick={() => wrap('```\n', '\n```')}>
          {'{ }'}
        </button>
        <button title="Citat" onClick={() => prefixLines('> ')}>
          ❝
        </button>
        <button title="Popis" onClick={() => prefixLines('• ')}>
          •≡
        </button>
      </div>

      {uploads.length > 0 && (
        <div className="uploads">
          {uploads.map((u) => (
            <div key={u.id} className={`upload ${u.error ? 'error' : ''}`}>
              <span className="file-name">{u.file.name}</span>
              {!u.ref && !u.error && <progress value={u.progress} max={1} />}
              {u.error && <span>greška</span>}
              <button className="icon" onClick={() => setUploads((x) => x.filter((y) => y.id !== u.id))} aria-label="Ukloni">
                ✕
              </button>
            </div>
          ))}
        </div>
      )}

      <textarea
        ref={ref}
        rows={1}
        value={text}
        placeholder={placeholder}
        onChange={(e) => {
          setText(e.target.value)
          updateMention(e.target.value, e.target.selectionStart)
          if (e.target.value.trim() && !hideTyping) sendTyping(space.id, threadName)
        }}
        onKeyDown={onKeyDown}
        onPaste={onPaste}
        onBlur={() => setTimeout(() => setMention(null), 150)}
      />

      <div className="composer-footer">
        <button className="icon" title="Priloži datoteku" onClick={() => fileInput.current?.click()}>
          📎
        </button>
        <button className="icon" title="Emoji" onClick={(e) => setPop({ kind: 'emoji', anchor: e.currentTarget })}>
          ☺
        </button>
        <button
          className="icon"
          title="Spomeni nekoga"
          onClick={() => {
            const caret = ref.current?.selectionStart ?? text.length
            const next = text.slice(0, caret) + (caret && !/\s$/.test(text.slice(0, caret)) ? ' @' : '@') + text.slice(caret)
            setText(next)
            setTimeout(() => {
              const pos = next.indexOf('@', caret) + 1
              ref.current?.focus()
              ref.current?.setSelectionRange(pos, pos)
              updateMention(next, pos)
            })
          }}
        >
          @
        </button>
        <input
          ref={fileInput}
          type="file"
          multiple
          hidden
          onChange={(e) => {
            if (e.target.files) addFiles(e.target.files)
            e.target.value = ''
          }}
        />
        <span className="spacer" />
        <div className="send-group">
          <button className="btn primary" onClick={send} disabled={(!text.trim() && !uploads.some((u) => u.ref)) || uploading} title="Pošalji (Enter)">
            Pošalji
          </button>
          <button
            className="btn primary caret"
            disabled={!text.trim()}
            title="Zakaži slanje"
            onClick={(e) => setPop({ kind: 'schedule', anchor: e.currentTarget })}
          >
            ▾
          </button>
        </div>
      </div>

      <div className="typing" aria-live="polite">
        {typingNames.length > 0 && (
          <>
            <span className="typing-dots">
              <i />
              <i />
              <i />
            </span>
            {typingText(typingNames)}
          </>
        )}
      </div>

      {mention && mentionResults.length > 0 && ref.current && (
        <Popover anchor={ref.current} onClose={() => setMention(null)}>
          <ul className="mention-list">
            {mentionResults.map((m, i) => (
              <li
                key={m.id}
                className={i === mention.index ? 'selected' : ''}
                onMouseDown={(e) => {
                  e.preventDefault()
                  pickMention(m)
                }}
              >
                <Avatar name={m.displayName ?? m.email ?? '?'} url={m.avatarUrl} size={22} />
                <span>{m.displayName ?? m.email}</span>
                {m.id === 'users/all' && <span className="muted">{m.email}</span>}
              </li>
            ))}
          </ul>
        </Popover>
      )}

      {pop && (
        <Popover anchor={pop.anchor} onClose={() => setPop(null)} align="right">
          {pop.kind === 'emoji' ? (
            <EmojiPicker
              onPick={(e) => {
                setPop(null)
                const caret = ref.current?.selectionStart ?? text.length
                setText(text.slice(0, caret) + e + text.slice(caret))
                setTimeout(() => ref.current?.focus())
              }}
            />
          ) : (
            <>
              <div className="menu-title">Zakaži slanje</div>
              <WhenPicker presets={schedulePresets} onPick={schedule} submitLabel="Zakaži" />
            </>
          )}
        </Popover>
      )}
    </div>
  )
}

