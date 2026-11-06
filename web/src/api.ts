export type SpaceKind = 0 | 1 | 2 // Space | GroupChat | DirectMessage
/** 0 Default, 1 All, 2 Mentions, 3 Nothing */
export type NotifyLevel = 0 | 1 | 2 | 3
/** 0 Mention, 1 DM, 2 ThreadReply, 3 Keyword, 4 Reaction, 5 Reminder, 6 AllMessages */
export type ActivityKind = 0 | 1 | 2 | 3 | 4 | 5 | 6

export interface Person {
  id: string
  displayName: string | null
  avatarUrl: string | null
  email: string | null
}

export interface Member extends Person {
  isAppUser: boolean
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
  mentionCount: number
  isStarred: boolean
  isMuted: boolean
  notifyLevel: NotifyLevel
  sectionId: string | null
  sortOrder: number
  dmUserName: string | null
}

export interface Reaction {
  /** unicode emoji or "custom:{uid}" */
  emoji: string
  count: number
  users: string[]
}

export interface Attachment {
  name?: string
  contentName?: string
  contentType?: string
  thumbnailUri?: string
  downloadUri?: string
  source?: string
  attachmentDataRef?: { resourceName?: string }
  driveDataRef?: { driveFileId?: string }
}

export interface Annotation {
  type: string
  startIndex?: number
  length?: number
  userMention?: { user?: { name?: string }; type?: string }
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
  reactions: Reaction[]
  attachments: Attachment[] | null
  annotations: Annotation[] | null
  createTime: string
  lastUpdateTime: string | null
  isDeleted: boolean
  isPinned: boolean
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

export interface Settings {
  keywords: string | null
  timeZone: string
  dndScheduleEnabled: boolean
  dndStart: string | null // "22:00:00"
  dndEnd: string | null
  snoozeUntil: string | null
  notifyThreadReplies: boolean
  pushOnlyWhenInactive: boolean
  statusEmoji: string | null
  statusText: string | null
  statusExpiresAt: string | null
  forceAway: boolean
  /** Reciprocal opt-in: share my read state; only then I see others'. */
  shareReadReceipts: boolean
  /** Read-only: false when the company switched read receipts off. */
  readReceiptsAvailable: boolean
  /** Don't show others that I'm typing (shown by default, like Slack). */
  hideTyping: boolean
}

export interface ReadReceipts {
  /** False when I don't share (reciprocity) or the company disabled it. */
  enabled: boolean
  readers: { userName: string; lastReadTime: string }[]
  /** Members we can't know about (not app users, or not sharing). */
  unknownCount: number
}

export interface Presence {
  userName: string
  state: 'active' | 'away' | 'offline'
  statusEmoji: string | null
  statusText: string | null
}

export interface ActivityItem {
  id: string
  kind: ActivityKind
  spaceId: string
  spaceName: string
  message: Message | null
  actor: Person | null
  preview: string | null
  createdAt: string
  isRead: boolean
}

export interface SavedItem {
  id: string
  message: Message
  spaceName: string
  createdAt: string
  remindAt: string | null
  completedAt: string | null
}

export interface ScheduledMessage {
  id: string
  spaceId: string
  spaceName: string
  threadName: string | null
  text: string
  sendAt: string
  status: 0 | 1 | 2 | 3 // Pending, Sent, Failed, Cancelled
  error: string | null
}

export interface Draft {
  spaceId: string
  threadName: string | null
  text: string
  updatedAt: string
}

export interface Section {
  id: string
  name: string
  sortOrder: number
  isCollapsed: boolean
}

export interface Bookmark {
  id: string
  title: string
  url: string
  createdAt: string
}

export interface LinkPreview {
  url: string
  title: string | null
  description: string | null
  imageUrl: string | null
  siteName: string | null
}

export interface SearchResult {
  message: Message
  spaceName: string
}

export interface NotificationPayload {
  title: string
  body: string
  spaceId: string
  threadName: string | null
  messageName: string
  kind: ActivityKind
  icon: string | null
  tag: string
}

export class UnauthorizedError extends Error {}
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message)
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const isForm = init?.body instanceof FormData
  const res = await fetch(url, {
    credentials: 'same-origin',
    headers: init?.body && !isForm ? { 'Content-Type': 'application/json' } : undefined,
    ...init,
  })
  if (res.status === 401) throw new UnauthorizedError('Not signed in')
  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new ApiError(res.status, text || `${res.status} ${res.statusText}`)
  }
  if (res.status === 204 || res.status === 202) return undefined as T
  return res.json() as Promise<T>
}

const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) })

export const api = {
  me: () => request<Me>('/api/me'),
  spaces: () => request<Space[]>('/api/spaces'),
  messages: (spaceId: string, before?: string, thread?: string) => {
    const q = new URLSearchParams({ limit: '50' })
    if (before) q.set('before', before)
    if (thread) q.set('thread', thread)
    return request<Message[]>(`/api/spaces/${spaceId}/messages?${q}`)
  },
  messagesAround: (spaceId: string, around: string) =>
    request<Message[]>(`/api/spaces/${spaceId}/messages?limit=60&around=${encodeURIComponent(around)}`),
  message: (id: string) => request<Message>(`/api/messages/${id}`),
  send: (spaceId: string, text: string, clientMessageId: string, threadName?: string | null, attachments?: unknown[]) =>
    request<Message>(`/api/spaces/${spaceId}/messages`, json('POST', { text, clientMessageId, threadName: threadName ?? null, attachments })),
  edit: (id: string, text: string) => request<Message>(`/api/messages/${id}`, json('PATCH', { text })),
  remove: (id: string) => request<void>(`/api/messages/${id}`, json('DELETE')),
  react: (id: string, emoji: string) => request<Message>(`/api/messages/${id}/reactions`, json('POST', { emoji })),
  unreact: (id: string, emoji: string) =>
    request<Message>(`/api/messages/${id}/reactions?emoji=${encodeURIComponent(emoji)}`, json('DELETE')),
  attachmentUrl: (messageId: string, index: number, download = false) =>
    `/api/messages/${messageId}/attachments/${index}${download ? '?download=true' : ''}`,
  unfurl: (url: string) => request<LinkPreview | undefined>(`/api/unfurl?url=${encodeURIComponent(url)}`),
  markRead: (spaceId: string, lastReadTime: string) => request<void>(`/api/spaces/${spaceId}/read`, json('POST', { lastReadTime })),
  readAll: () => request<void>('/api/read-all', json('POST')),
  resync: () => request<void>('/api/sync', json('POST')),
  logout: () => request<void>('/auth/logout', json('POST')),

  // settings, presence, push
  settings: () => request<Settings>('/api/settings'),
  saveSettings: (s: Settings) => request<Settings>('/api/settings', json('PUT', s)),
  snooze: (minutes: number | null) => request<Settings>('/api/settings/snooze', json('POST', { minutes })),
  presence: () => request<Presence[]>('/api/presence'),
  readReceipts: (spaceId: string) => request<ReadReceipts>(`/api/spaces/${spaceId}/read-receipts`),
  pushKey: () => request<{ publicKey: string } | undefined>('/api/push/key'),
  pushSubscribe: (sub: PushSubscriptionJSON) =>
    request<void>('/api/push/subscribe', json('POST', { endpoint: sub.endpoint, keys: sub.keys, userAgent: navigator.userAgent })),
  pushUnsubscribe: (endpoint: string) => request<void>('/api/push/unsubscribe', json('POST', { endpoint })),
  pushTest: () => request<{ delivered: number }>('/api/push/test', json('POST')),

  // activity, sections, prefs
  activity: () => request<{ items: ActivityItem[]; unread: number }>('/api/activity'),
  activityRead: (ids?: string[]) => request<void>('/api/activity/read', json('POST', { ids: ids ?? null })),
  sections: () => request<Section[]>('/api/sections'),
  createSection: (name: string) => request<Section>('/api/sections', json('POST', { name })),
  updateSection: (id: string, patch: Partial<Pick<Section, 'name' | 'sortOrder' | 'isCollapsed'>>) =>
    request<void>(`/api/sections/${id}`, json('PATCH', patch)),
  deleteSection: (id: string) => request<void>(`/api/sections/${id}`, json('DELETE')),
  spacePrefs: (
    spaceId: string,
    prefs: { notifyLevel?: NotifyLevel; isMuted?: boolean; isStarred?: boolean; sectionId?: string; clearSection?: boolean; sortOrder?: number },
  ) => request<void>(`/api/spaces/${spaceId}/prefs`, json('PUT', prefs)),

  // people & spaces
  searchPeople: (q: string) => request<Member[]>(`/api/people/search?q=${encodeURIComponent(q)}`),
  members: (spaceId: string) => request<Member[]>(`/api/spaces/${spaceId}/members`),
  createSpace: (kind: 'dm' | 'group' | 'space', members: string[], displayName?: string) =>
    request<{ id: string }>('/api/spaces', json('POST', { kind, members, displayName })),
  addMembers: (spaceId: string, members: string[]) => request<void>(`/api/spaces/${spaceId}/members`, json('POST', { members })),

  // pins & bookmarks
  pins: (spaceId: string) => request<Message[]>(`/api/spaces/${spaceId}/pins`),
  pin: (spaceId: string, messageId: string) => request<void>(`/api/spaces/${spaceId}/pins`, json('POST', { messageId })),
  unpin: (spaceId: string, messageId: string) => request<void>(`/api/spaces/${spaceId}/pins/${messageId}`, json('DELETE')),
  bookmarks: (spaceId: string) => request<Bookmark[]>(`/api/spaces/${spaceId}/bookmarks`),
  addBookmark: (spaceId: string, title: string, url: string) =>
    request<Bookmark>(`/api/spaces/${spaceId}/bookmarks`, json('POST', { title, url })),
  removeBookmark: (spaceId: string, id: string) => request<void>(`/api/spaces/${spaceId}/bookmarks/${id}`, json('DELETE')),

  // search, later, scheduled, drafts
  search: (q: string, spaceId?: string) =>
    request<SearchResult[]>(`/api/search?q=${encodeURIComponent(q)}${spaceId ? `&spaceId=${spaceId}` : ''}`),
  saved: (completed = false) => request<SavedItem[]>(`/api/saved?completed=${completed}`),
  save: (messageId: string, remindAt?: string | null) => request<{ id: string }>('/api/saved', json('POST', { messageId, remindAt: remindAt ?? null })),
  updateSaved: (id: string, patch: { remindAt?: string; clearReminder?: boolean; completed?: boolean }) =>
    request<void>(`/api/saved/${id}`, json('PATCH', patch)),
  removeSaved: (id: string) => request<void>(`/api/saved/${id}`, json('DELETE')),
  scheduled: () => request<ScheduledMessage[]>('/api/scheduled'),
  schedule: (spaceId: string, text: string, sendAt: string, threadName?: string | null) =>
    request<{ id: string }>('/api/scheduled', json('POST', { spaceId, text, sendAt, threadName: threadName ?? null })),
  updateScheduled: (id: string, patch: { text?: string; sendAt?: string }) => request<void>(`/api/scheduled/${id}`, json('PATCH', patch)),
  cancelScheduled: (id: string) => request<void>(`/api/scheduled/${id}`, json('DELETE')),
  drafts: () => request<Draft[]>('/api/drafts'),
  saveDraft: (spaceId: string, threadName: string | null | undefined, text: string) =>
    request<void>('/api/drafts', json('PUT', { spaceId, threadName: threadName ?? null, text })),
}

/** Uploads a file with progress; resolves to the opaque upload reference for api.send(..., attachments). */
export function uploadAttachment(spaceId: string, file: File, onProgress: (p: number) => void): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `/api/spaces/${spaceId}/attachments`)
    xhr.withCredentials = true
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(e.loaded / e.total)
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve(JSON.parse(xhr.responseText)) : reject(new ApiError(xhr.status, xhr.responseText)))
    xhr.onerror = () => reject(new Error('Upload failed'))
    const fd = new FormData()
    fd.append('file', file)
    xhr.send(fd)
  })
}

export const loginUrl = (returnUrl = location.pathname) => `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`

export const newClientId = () => `client-${crypto.randomUUID()}`

export const messageLink = (spaceId: string, messageId: string) => `${location.origin}/#/space/${spaceId}/m/${messageId}`
