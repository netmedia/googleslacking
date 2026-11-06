import { useQuery } from '@tanstack/react-query'
import { api, type Presence } from './api'
import { keys } from './cache'

export const useSettings = () => useQuery({ queryKey: keys.settings, queryFn: api.settings, staleTime: 60_000 })
export const useSections = () => useQuery({ queryKey: keys.sections, queryFn: api.sections, staleTime: 60_000 })
export const useActivity = () => useQuery({ queryKey: keys.activity, queryFn: api.activity, staleTime: 30_000 })
export const useMembers = (spaceId: string | null | undefined) =>
  useQuery({ queryKey: keys.members(spaceId ?? ''), queryFn: () => api.members(spaceId!), enabled: !!spaceId, staleTime: 5 * 60_000 })

export function usePresence() {
  const q = useQuery({ queryKey: keys.presence, queryFn: api.presence, staleTime: 30_000, refetchInterval: 60_000 })
  const map = new Map<string, Presence>()
  for (const p of q.data ?? []) map.set(p.userName, p)
  return map
}

/** True while notifications are paused (snooze). */
export function useSnoozed() {
  const s = useSettings().data
  return !!s?.snoozeUntil && Date.parse(s.snoozeUntil) > Date.now()
}
