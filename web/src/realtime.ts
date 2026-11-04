import { useEffect, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { QueryClient } from '@tanstack/react-query'
import type { Me, Message, Space } from './api'
import { keys, upsertMessages } from './cache'

export type ConnectionStatus = 'connecting' | 'online' | 'reconnecting' | 'offline'

/**
 * One SignalR connection per app. Server pushes:
 *  - messagesChanged(spaceId, Message[])
 *  - spaceChanged(spaceId)
 *  - spacesChanged()
 */
export function useRealtime(qc: QueryClient, me: Me | undefined, activeSpaceId: string | null) {
  const [status, setStatus] = useState<ConnectionStatus>('connecting')

  useEffect(() => {
    if (!me) return
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/chat')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    conn.on('messagesChanged', (spaceId: string, messages: Message[]) => {
      upsertMessages(qc, spaceId, messages)
      qc.invalidateQueries({ queryKey: keys.spaces })
      notify(qc, me, spaceId, messages)
    })
    conn.on('spaceChanged', () => qc.invalidateQueries({ queryKey: keys.spaces }))
    conn.on('spacesChanged', async () => {
      await qc.invalidateQueries({ queryKey: keys.spaces })
      if (conn.state === HubConnectionState.Connected) await conn.invoke('JoinSpaces')
    })

    conn.onreconnecting(() => setStatus('reconnecting'))
    conn.onreconnected(() => {
      setStatus('online')
      // We may have missed pushes while disconnected: refetch from the (server-side) cache.
      qc.invalidateQueries({ queryKey: keys.spaces })
      qc.invalidateQueries({ queryKey: ['messages'] })
    })
    conn.onclose(() => setStatus('offline'))

    let stopped = false
    const start = async () => {
      try {
        await conn.start()
        setStatus('online')
      } catch {
        if (!stopped) setTimeout(start, 5000)
      }
    }
    start()
    return () => {
      stopped = true
      conn.stop()
    }
  }, [qc, me])

  // Keep the active space id visible to the notification handler without reconnecting.
  useEffect(() => {
    activeSpaceRef.current = activeSpaceId
  }, [activeSpaceId])

  return status
}

const activeSpaceRef: { current: string | null } = { current: null }

function notify(qc: QueryClient, me: Me, spaceId: string, messages: Message[]) {
  if (typeof Notification === 'undefined' || Notification.permission !== 'granted') return
  if (document.visibilityState === 'visible' && document.hasFocus() && activeSpaceRef.current === spaceId) return

  const space = qc.getQueryData<Space[]>(keys.spaces)?.find((s) => s.id === spaceId)
  if (space?.isMuted) return

  const fresh = messages.filter(
    (m) => !m.isDeleted && m.sender.id !== me.googleUserName && !m.lastUpdateTime && Date.now() - Date.parse(m.createTime) < 60_000,
  )
  for (const m of fresh.slice(0, 3)) {
    const n = new Notification(space?.displayName ?? 'New message', {
      body: `${m.sender.displayName ?? 'Someone'}: ${m.text ?? ''}`.slice(0, 200),
      tag: m.googleName,
      icon: m.sender.avatarUrl ?? '/icon.svg',
    })
    n.onclick = () => {
      window.focus()
      location.hash = `#/space/${spaceId}`
    }
  }
}
