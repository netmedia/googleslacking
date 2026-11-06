/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute, createHandlerBoundToURL } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: (string | { url: string; revision: string | null })[] }

// App shell precache (injected at build time by vite-plugin-pwa).
cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)
registerRoute(new NavigationRoute(createHandlerBoundToURL('/index.html'), { denylist: [/^\/api/, /^\/auth/, /^\/hubs/, /^\/healthz/] }))

self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()))

interface Payload {
  title: string
  body: string
  spaceId: string
  threadName: string | null
  messageName: string
  icon: string | null
  tag: string
}

const urlFor = (p: Pick<Payload, 'spaceId' | 'threadName'>) =>
  `/#/space/${p.spaceId}${p.threadName ? `/t/${encodeURIComponent(p.threadName)}` : ''}`

// Web Push from the backend (only sent when no client is active — see NotificationService).
self.addEventListener('push', (event) => {
  if (!event.data) return
  let p: Payload
  try {
    p = event.data.json() as Payload
  } catch {
    return
  }
  event.waitUntil(
    (async () => {
      // Skip if a focused window is already showing this conversation.
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      const focusedHere = windows.some((w) => (w as WindowClient).focused && new URL(w.url).hash.startsWith(`#/space/${p.spaceId}`))
      if (focusedHere) return
      await self.registration.showNotification(p.title, {
        body: p.body,
        tag: p.tag, // same tag as the in-app notification -> no duplicates
        icon: p.icon ?? '/icon.svg',
        badge: '/icon.svg',
        data: { url: urlFor(p) },
      })
      // App icon badge (installed PWA).
      const nav = self.navigator as unknown as { setAppBadge?: () => Promise<void> }
      await nav.setAppBadge?.().catch(() => {})
    })(),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = (event.notification.data as { url?: string } | undefined)?.url ?? '/'
  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      const existing = windows[0] as WindowClient | undefined
      if (existing) {
        await existing.focus()
        existing.postMessage({ type: 'navigate', url })
      } else {
        await self.clients.openWindow(url)
      }
    })(),
  )
})
