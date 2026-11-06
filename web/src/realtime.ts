import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import type { QueryClient } from '@tanstack/react-query'
import type { Draft, Me, Message, NotificationPayload } from './api'
import { keys, upsertMessages } from './cache'
import { showLocalNotification } from './push'
import { typingReceived, typingStopped } from './typing'

export type ConnectionStatus = 'connecting' | 'online' | 'reconnecting' | 'offline'

const HEARTBEAT_MS = 30_000

let current: HubConnection | null = null
const TYPING_THROTTLE_MS = 3000
let lastTyping = { key: '', at: 0 }

/** Called by the composer on input; throttled so we send at most one signal per ~3 s per conversation. */
export function sendTyping(spaceId: string, threadName: string | null | undefined) {
  const key = `${spaceId}:${threadName ?? ''}`
  const now = Date.now()
  if (lastTyping.key === key && now - lastTyping.at < TYPING_THROTTLE_MS) return
  lastTyping = { key, at: now }
  if (current?.state === HubConnectionState.Connected) current.invoke('Typing', spaceId, threadName ?? null).catch(() => {})
}

/** After sending a message, the next keystroke should signal immediately again. */
export function resetTyping() {
  lastTyping = { key: '', at: 0 }
}

/** One SignalR connection per app; see ChatHub for the list of events. */
export function useRealtime(qc: QueryClient, me: Me | undefined, activeSpaceId: string | null) {
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const connRef = useRef<HubConnection | null>(null)
  const activeRef = useRef(activeSpaceId)
  activeRef.current = activeSpaceId

  useEffect(() => {
    if (!me) return
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/chat')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()
    connRef.current = conn
    current = conn

    const heartbeat = () => {
      if (conn.state === HubConnectionState.Connected)
        conn.invoke('Heartbeat', document.visibilityState === 'visible' && document.hasFocus(), activeRef.current).catch(() => {})
    }

    conn.on('messagesChanged', (spaceId: string, messages: Message[]) => {
      upsertMessages(qc, spaceId, messages)
      for (const m of messages) typingStopped(spaceId, m.sender.id)
      qc.invalidateQueries({ queryKey: keys.spaces })
    })
    conn.on('spaceChanged', () => qc.invalidateQueries({ queryKey: keys.spaces }))
    conn.on('spacesChanged', async () => {
      await qc.invalidateQueries({ queryKey: keys.spaces })
      if (conn.state === HubConnectionState.Connected) await conn.invoke('JoinSpaces')
    })
    conn.on('notification', (p: NotificationPayload) => {
      const lookingAtIt = document.visibilityState === 'visible' && document.hasFocus() && activeRef.current === p.spaceId
      if (lookingAtIt) return
      const url = `/#/space/${p.spaceId}${p.threadName ? `/t/${encodeURIComponent(p.threadName)}` : ''}`
      void showLocalNotification(p.title, p.body, p.tag, url, p.icon)
    })
    conn.on('activityChanged', () => qc.invalidateQueries({ queryKey: keys.activity }))
    conn.on('savedChanged', () => qc.invalidateQueries({ queryKey: ['saved'] }))
    conn.on('scheduledChanged', () => qc.invalidateQueries({ queryKey: keys.scheduled }))
    conn.on('settingsChanged', () => qc.invalidateQueries({ queryKey: keys.settings }))
    conn.on('sectionsChanged', () => qc.invalidateQueries({ queryKey: keys.sections }))
    conn.on('presenceChanged', () => qc.invalidateQueries({ queryKey: keys.presence }))
    conn.on('pinsChanged', (spaceId: string) => qc.invalidateQueries({ queryKey: keys.pins(spaceId) }))
    conn.on('bookmarksChanged', (spaceId: string) => qc.invalidateQueries({ queryKey: keys.bookmarks(spaceId) }))
    conn.on('typing', (spaceId: string, threadName: string | null, userName: string) => {
      if (userName !== me.googleUserName) typingReceived(spaceId, threadName, userName)
    })
    conn.on('readReceiptsChanged', (spaceId: string) => qc.invalidateQueries({ queryKey: keys.receipts(spaceId) }))
    conn.on('draftsChanged', (d: Draft) =>
      qc.setQueryData<Draft[]>(keys.drafts, (list) => {
        const rest = (list ?? []).filter((x) => !(x.spaceId === d.spaceId && (x.threadName ?? null) === (d.threadName ?? null)))
        return d.text ? [d, ...rest] : rest
      }),
    )

    conn.onreconnecting(() => setStatus('reconnecting'))
    conn.onreconnected(() => {
      setStatus('online')
      heartbeat()
      // We may have missed pushes while disconnected: refetch from the (server-side) cache.
      qc.invalidateQueries({ queryKey: keys.spaces })
      qc.invalidateQueries({ queryKey: ['messages'] })
      qc.invalidateQueries({ queryKey: keys.activity })
    })
    conn.onclose(() => setStatus('offline'))

    let stopped = false
    const start = async () => {
      try {
        await conn.start()
        setStatus('online')
        heartbeat()
      } catch {
        if (!stopped) setTimeout(start, 5000)
      }
    }
    start()

    const timer = setInterval(heartbeat, HEARTBEAT_MS)
    window.addEventListener('focus', heartbeat)
    window.addEventListener('blur', heartbeat)
    document.addEventListener('visibilitychange', heartbeat)
    return () => {
      stopped = true
      clearInterval(timer)
      window.removeEventListener('focus', heartbeat)
      window.removeEventListener('blur', heartbeat)
      document.removeEventListener('visibilitychange', heartbeat)
      conn.stop()
      connRef.current = null
      if (current === conn) current = null
    }
  }, [qc, me])

  // Tell the server which space we're looking at (suppresses notifications for it).
  useEffect(() => {
    const conn = connRef.current
    if (conn?.state === HubConnectionState.Connected)
      conn.invoke('Heartbeat', document.visibilityState === 'visible' && document.hasFocus(), activeSpaceId).catch(() => {})
  }, [activeSpaceId])

  return status
}
