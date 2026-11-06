import type { InfiniteData, QueryClient } from '@tanstack/react-query'
import type { Message, Space } from './api'

export const keys = {
  me: ['me'] as const,
  spaces: ['spaces'] as const,
  messages: (spaceId: string) => ['messages', spaceId] as const,
  thread: (spaceId: string, threadName: string) => ['thread', spaceId, threadName] as const,
  settings: ['settings'] as const,
  presence: ['presence'] as const,
  sections: ['sections'] as const,
  activity: ['activity'] as const,
  saved: (completed = false) => ['saved', completed] as const,
  scheduled: ['scheduled'] as const,
  drafts: ['drafts'] as const,
  members: (spaceId: string) => ['members', spaceId] as const,
  pins: (spaceId: string) => ['pins', spaceId] as const,
  bookmarks: (spaceId: string) => ['bookmarks', spaceId] as const,
  receipts: (spaceId: string) => ['receipts', spaceId] as const,
  unfurl: (url: string) => ['unfurl', url] as const,
  search: (q: string, spaceId?: string) => ['search', q, spaceId ?? ''] as const,
}

export type Pages = InfiniteData<Message[], string | undefined>

const byNewest = (a: Message, b: Message) => b.createTime.localeCompare(a.createTime)

const sameMessage = (a: Message, b: Message) =>
  a.googleName === b.googleName ||
  a.id === b.id ||
  (!!a.clientAssignedMessageId && a.clientAssignedMessageId === b.clientAssignedMessageId)

/** Insert or replace messages in a newest-first list (matches optimistic copies by client id). */
export function mergeInto(list: Message[], incoming: Message[]): Message[] {
  const out = [...list]
  for (const m of incoming) {
    const i = out.findIndex((x) => sameMessage(x, m))
    if (i >= 0) out[i] = { ...m, pending: false, failed: false }
    else out.push(m)
  }
  return out.sort(byNewest)
}

/** Applies server/realtime messages to the space timeline and any open thread views. */
export function upsertMessages(qc: QueryClient, spaceId: string, incoming: Message[]) {
  qc.setQueryData<Pages>(keys.messages(spaceId), (data) => {
    if (!data) return data
    const pages = data.pages.map((p) => [...p])
    const rest: Message[] = []
    for (const m of incoming) {
      const pi = pages.findIndex((p) => p.some((x) => sameMessage(x, m)))
      if (pi >= 0) pages[pi] = mergeInto(pages[pi], [m])
      else rest.push(m)
    }
    if (rest.length) pages[0] = mergeInto(pages[0] ?? [], rest)
    return { ...data, pages }
  })

  for (const m of incoming) {
    if (!m.threadName) continue
    qc.setQueryData<Message[]>(keys.thread(spaceId, m.threadName), (list) => (list ? mergeInto(list, [m]) : list))
  }
}

export function markFailed(qc: QueryClient, spaceId: string, clientId: string) {
  qc.setQueryData<Pages>(keys.messages(spaceId), (data) =>
    data && {
      ...data,
      pages: data.pages.map((p) => p.map((m) => (m.clientAssignedMessageId === clientId ? { ...m, failed: true } : m))),
    },
  )
}

export function setSpaceRead(qc: QueryClient, spaceId: string, lastReadTime: string) {
  qc.setQueryData<Space[]>(keys.spaces, (list) =>
    list?.map((s) => (s.id === spaceId ? { ...s, unreadCount: 0, mentionCount: 0, lastReadTime } : s)),
  )
}

export function findLoadedMessage(qc: QueryClient, spaceId: string, pred: (m: Message) => boolean): Message | undefined {
  return qc.getQueryData<Pages>(keys.messages(spaceId))?.pages.flat().find(pred)
}
