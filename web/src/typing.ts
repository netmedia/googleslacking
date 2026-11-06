import { useEffect, useState, useSyncExternalStore } from 'react'

/** Who is typing where. Entries expire 5 s after the last signal (clients re-send every ~3 s). */
const EXPIRE_MS = 5000

type Key = string // `${spaceId}:${threadName ?? ''}`
const state = new Map<Key, Map<string, number>>() // key -> userName -> expiresAt
const listeners = new Set<() => void>()
let version = 0
const emit = () => {
  version++
  listeners.forEach((l) => l())
}

const keyOf = (spaceId: string, threadName: string | null | undefined) => `${spaceId}:${threadName ?? ''}`

export function typingReceived(spaceId: string, threadName: string | null, userName: string) {
  const k = keyOf(spaceId, threadName)
  const users = state.get(k) ?? new Map<string, number>()
  users.set(userName, Date.now() + EXPIRE_MS)
  state.set(k, users)
  emit()
}

/** A message from this user arrived: they're no longer typing (in any thread of that space). */
export function typingStopped(spaceId: string, userName: string) {
  let changed = false
  for (const [k, users] of state) if (k.startsWith(spaceId + ':') && users.delete(userName)) changed = true
  if (changed) emit()
}

/** User names currently typing in this conversation (or thread). */
export function useTyping(spaceId: string, threadName?: string | null): string[] {
  useSyncExternalStore(
    (l) => {
      listeners.add(l)
      return () => {
        listeners.delete(l)
      }
    },
    () => version,
  )
  // Re-render once a second so expired entries disappear.
  const [, tick] = useState(0)
  const k = keyOf(spaceId, threadName)
  const users = state.get(k)
  const active = users ? [...users].filter(([, exp]) => exp > Date.now()).map(([u]) => u) : []
  useEffect(() => {
    if (!active.length) return
    const t = setInterval(() => tick((n) => n + 1), 1000)
    return () => clearInterval(t)
  }, [active.length])
  return active
}

/** "Marko tipka…", "Marko i Ana tipkaju…", "Marko, Ana i još 2 tipkaju…" */
export function typingText(names: string[]) {
  if (!names.length) return ''
  if (names.length === 1) return `${names[0]} tipka…`
  if (names.length === 2) return `${names[0]} i ${names[1]} tipkaju…`
  if (names.length === 3) return `${names[0]}, ${names[1]} i ${names[2]} tipkaju…`
  return `${names[0]}, ${names[1]} i još ${names.length - 2} tipkaju…`
}
