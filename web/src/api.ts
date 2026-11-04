export type SpaceKind = 0 | 1 | 2 // Space | GroupChat | DirectMessage

export interface Person {
  id: string
  displayName: string | null
  avatarUrl: string | null
  email: string | null
}

export interface Space {
  id: string
  googleName: string
  displayName: string
  kind: SpaceKind
  isThreaded: boolean
  lastActiveTime: string | null
  lastReadTime: string | null
  unreadCount: number
  isStarred: boolean
  isMuted: boolean
}

export interface Reaction {
  emoji?: { unicode?: string; customEmoji?: { uid?: string } }
  reactionCount?: number
}

export interface Message {
  id: string
  spaceId: string
  googleName: string
  threadName: string | null
  isThreadReply: boolean
  sender: Person
  text: string | null
  formattedText: string | null
  reactions: Reaction[] | null
  attachments: unknown[] | null
  createTime: string
  lastUpdateTime: string | null
  isDeleted: boolean
  clientAssignedMessageId: string | null
  /** client-only: optimistic message not yet confirmed by the server */
  pending?: boolean
  failed?: boolean
}

export interface Me {
  id: string
  googleUserName: string
  email: string
  displayName: string | null
  avatarUrl: string | null
  syncCompleted: boolean
}

export class UnauthorizedError extends Error {}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    credentials: 'same-origin',
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
    ...init,
  })
  if (res.status === 401) throw new UnauthorizedError('Not signed in')
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  if (res.status === 204 || res.status === 202) return undefined as T
  return res.json() as Promise<T>
}

export const api = {
  me: () => request<Me>('/api/me'),
  spaces: () => request<Space[]>('/api/spaces'),
  messages: (spaceId: string, before?: string, thread?: string) => {
    const q = new URLSearchParams({ limit: '50' })
    if (before) q.set('before', before)
    if (thread) q.set('thread', thread)
    return request<Message[]>(`/api/spaces/${spaceId}/messages?${q}`)
  },
  send: (spaceId: string, text: string, clientMessageId: string, threadName?: string | null) =>
    request<Message>(`/api/spaces/${spaceId}/messages`, {
      method: 'POST',
      body: JSON.stringify({ text, clientMessageId, threadName: threadName ?? null }),
    }),
  markRead: (spaceId: string, lastReadTime: string) =>
    request<void>(`/api/spaces/${spaceId}/read`, { method: 'POST', body: JSON.stringify({ lastReadTime }) }),
  resync: () => request<void>('/api/sync', { method: 'POST' }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
}

export const loginUrl = (returnUrl = location.pathname) => `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`

export const newClientId = () => `client-${crypto.randomUUID()}`
