import { api } from './api'

export type PushState = 'unsupported' | 'unconfigured' | 'denied' | 'enabled' | 'disabled'

const supported = () => 'serviceWorker' in navigator && 'PushManager' in window && typeof Notification !== 'undefined'

function keyToBytes(base64url: string) {
  const b64 = base64url.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(base64url.length / 4) * 4, '=')
  return Uint8Array.from(atob(b64), (c) => c.charCodeAt(0))
}

export async function getPushState(): Promise<PushState> {
  if (!supported()) return 'unsupported'
  if (Notification.permission === 'denied') return 'denied'
  const key = await api.pushKey().catch(() => undefined)
  if (!key) return 'unconfigured'
  const reg = await navigator.serviceWorker.ready
  return (await reg.pushManager.getSubscription()) ? 'enabled' : 'disabled'
}

export async function enablePush(): Promise<PushState> {
  if (!supported()) return 'unsupported'
  const permission = await Notification.requestPermission()
  if (permission !== 'granted') return 'denied'
  const key = await api.pushKey()
  if (!key) return 'unconfigured'
  const reg = await navigator.serviceWorker.ready
  const sub =
    (await reg.pushManager.getSubscription()) ??
    (await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyToBytes(key.publicKey) }))
  await api.pushSubscribe(sub.toJSON())
  return 'enabled'
}

export async function disablePush(): Promise<PushState> {
  if (!supported()) return 'unsupported'
  const reg = await navigator.serviceWorker.ready
  const sub = await reg.pushManager.getSubscription()
  if (sub) {
    await api.pushUnsubscribe(sub.endpoint).catch(() => {})
    await sub.unsubscribe()
  }
  return 'disabled'
}

/** On start: make sure the server still knows this device's subscription (it may have been pruned). */
export async function resyncPush() {
  if (!supported() || Notification.permission !== 'granted') return
  const reg = await navigator.serviceWorker.ready
  const sub = await reg.pushManager.getSubscription()
  if (sub) await api.pushSubscribe(sub.toJSON()).catch(() => {})
}

/** Shows a system notification via the service worker (same tag as Web Push = no duplicates). */
export async function showLocalNotification(title: string, body: string, tag: string, url: string, icon?: string | null) {
  if (typeof Notification === 'undefined' || Notification.permission !== 'granted') return
  try {
    const reg = await navigator.serviceWorker?.ready
    if (reg) {
      await reg.showNotification(title, { body, tag, icon: icon ?? '/icon.svg', data: { url } })
      return
    }
  } catch {
    /* fall through */
  }
  const n = new Notification(title, { body, tag, icon: icon ?? '/icon.svg' })
  n.onclick = () => {
    window.focus()
    location.hash = url.replace(/^\//, '')
  }
}
