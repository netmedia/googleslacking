/** Date helpers (Croatian locale) and reminder/schedule presets. */

export const fmtTime = (iso: string) => new Date(iso).toLocaleTimeString('hr-HR', { hour: '2-digit', minute: '2-digit' })

export const fmtDay = (iso: string) =>
  new Date(iso).toLocaleDateString('hr-HR', { weekday: 'long', day: 'numeric', month: 'long' })

export function fmtWhen(iso: string) {
  const d = new Date(iso)
  const now = new Date()
  const tomorrow = new Date(now)
  tomorrow.setDate(now.getDate() + 1)
  const time = fmtTime(iso)
  if (d.toDateString() === now.toDateString()) return `danas u ${time}`
  if (d.toDateString() === tomorrow.toDateString()) return `sutra u ${time}`
  return `${d.toLocaleDateString('hr-HR', { weekday: 'short', day: 'numeric', month: 'numeric' })} u ${time}`
}

export function fmtRelative(iso: string) {
  const diff = (Date.now() - Date.parse(iso)) / 1000
  if (diff < 60) return 'upravo'
  if (diff < 3600) return `prije ${Math.floor(diff / 60)} min`
  if (diff < 86400) return `prije ${Math.floor(diff / 3600)} h`
  return new Date(iso).toLocaleDateString('hr-HR', { day: 'numeric', month: 'numeric' })
}

const at = (days: number, hour: number, minute = 0) => {
  const d = new Date()
  d.setDate(d.getDate() + days)
  d.setHours(hour, minute, 0, 0)
  return d
}

function nextMonday9() {
  const d = at(0, 9)
  const add = ((8 - d.getDay()) % 7) || 7
  d.setDate(d.getDate() + add)
  return d
}

export interface Preset {
  label: string
  at: () => Date
}

export const reminderPresets: Preset[] = [
  { label: 'Za 20 minuta', at: () => new Date(Date.now() + 20 * 60_000) },
  { label: 'Za 1 sat', at: () => new Date(Date.now() + 60 * 60_000) },
  { label: 'Za 3 sata', at: () => new Date(Date.now() + 3 * 60 * 60_000) },
  { label: 'Sutra u 9:00', at: () => at(1, 9) },
  { label: 'Sljedeći ponedjeljak u 9:00', at: nextMonday9 },
]

export const schedulePresets: Preset[] = [
  { label: 'Sutra u 9:00', at: () => at(1, 9) },
  { label: 'Sutra u 13:00', at: () => at(1, 13) },
  { label: 'Sljedeći ponedjeljak u 9:00', at: nextMonday9 },
]

export const snoozePresets = (): { label: string; minutes: number }[] => [
  { label: '30 minuta', minutes: 30 },
  { label: '1 sat', minutes: 60 },
  { label: '2 sata', minutes: 120 },
  { label: 'Do sutra (9:00)', minutes: Math.max(1, Math.round((at(1, 9).getTime() - Date.now()) / 60_000)) },
]

/** "2026-11-04T10:30" for <input type="datetime-local"> */
export const toLocalInput = (d: Date) => {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
