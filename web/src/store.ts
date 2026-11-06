import { useSyncExternalStore } from 'react'

/** Tiny global UI store (no extra dependency). */
function createStore<T>(initial: T) {
  let state = initial
  const listeners = new Set<() => void>()
  return {
    get: () => state,
    set: (next: T | ((s: T) => T)) => {
      state = typeof next === 'function' ? (next as (s: T) => T)(state) : next
      listeners.forEach((l) => l())
    },
    subscribe: (l: () => void) => {
      listeners.add(l)
      return () => listeners.delete(l)
    },
  }
}

export type Panel =
  | { type: 'thread'; threadName: string }
  | { type: 'search'; query?: string }
  | { type: 'activity' }
  | { type: 'later'; tab?: 'saved' | 'scheduled' | 'drafts' }
  | { type: 'pins' }
  | { type: 'members' }
  | null

export type Modal = 'settings' | 'new' | 'shortcuts' | null

export interface UiState {
  panel: Panel
  modal: Modal
  /** Message being forwarded (opens the space picker). */
  forward: import('./api').Message | null
  /** Scroll to + highlight this message after opening its space. */
  jumpTo: { spaceId: string; messageId: string; createTime: string } | null
}

export const ui = createStore<UiState>({ panel: null, modal: null, jumpTo: null, forward: null })
export const useUi = <S,>(sel: (s: UiState) => S) => useSyncExternalStore(ui.subscribe, () => sel(ui.get()))
export const setPanel = (panel: Panel) => ui.set((s) => ({ ...s, panel }))
export const togglePanel = (panel: Exclude<Panel, null>) =>
  ui.set((s) => ({ ...s, panel: s.panel?.type === panel.type ? null : panel }))
export const setModal = (modal: Modal) => ui.set((s) => ({ ...s, modal }))
export const jumpTo = (spaceId: string, messageId: string, createTime: string) => {
  ui.set((s) => ({ ...s, jumpTo: { spaceId, messageId, createTime } }))
  location.hash = `#/space/${spaceId}`
}

// ---- Toasts ----
export interface Toast {
  id: number
  text: string
  kind: 'info' | 'error'
}
export const toasts = createStore<Toast[]>([])
export const useToasts = () => useSyncExternalStore(toasts.subscribe, toasts.get)
let toastId = 0
export function toast(text: string, kind: Toast['kind'] = 'info') {
  const id = ++toastId
  toasts.set((t) => [...t, { id, text, kind }])
  setTimeout(() => toasts.set((t) => t.filter((x) => x.id !== id)), kind === 'error' ? 6000 : 3500)
}

// ---- Composer bus (quote / forward / focus from elsewhere) ----
type ComposerEvent = { type: 'insert'; spaceId: string; threadName?: string | null; text: string } | { type: 'editLast'; spaceId: string }
const composerListeners = new Set<(e: ComposerEvent) => void>()
export const composerBus = {
  emit: (e: ComposerEvent) => composerListeners.forEach((l) => l(e)),
  on: (l: (e: ComposerEvent) => void) => {
    composerListeners.add(l)
    return () => {
      composerListeners.delete(l)
    }
  },
}

// ---- Safe localStorage ----
export const storage = {
  get<T>(key: string, fallback: T): T {
    try {
      const v = localStorage.getItem(key)
      return v === null ? fallback : (JSON.parse(v) as T)
    } catch {
      return fallback
    }
  },
  set(key: string, value: unknown) {
    try {
      if (value === undefined || value === null || value === '') localStorage.removeItem(key)
      else localStorage.setItem(key, JSON.stringify(value))
    } catch {
      /* unavailable */
    }
  },
}
