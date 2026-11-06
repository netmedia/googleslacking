import type { QueryClient } from '@tanstack/react-query'
import { api, type NotifyLevel, type Section, type Space } from '../api'
import { keys, setSpaceRead } from '../cache'
import type { MenuItem } from './ui'

const refresh = (qc: QueryClient) => () => qc.invalidateQueries({ queryKey: keys.spaces })

export const notifyLabel = (space: Space) =>
  ({ 0: space.kind === 0 ? 'Zadano (spominjanja)' : 'Zadano (sve)', 1: 'Sve nove poruke', 2: 'Samo spominjanja', 3: 'Ništa' })[space.notifyLevel]

export function notifyMenu(space: Space, qc: QueryClient): MenuItem[] {
  const set = (notifyLevel: NotifyLevel) => api.spacePrefs(space.id, { notifyLevel }).then(refresh(qc))
  return [
    { label: space.kind === 0 ? 'Zadano (spominjanja i ključne riječi)' : 'Zadano (sve poruke)', checked: space.notifyLevel === 0, onClick: () => set(0) },
    { label: 'Sve nove poruke', checked: space.notifyLevel === 1, onClick: () => set(1) },
    { label: 'Samo spominjanja', checked: space.notifyLevel === 2, onClick: () => set(2) },
    { label: 'Ništa', checked: space.notifyLevel === 3, onClick: () => set(3) },
    { divider: true, label: '' },
    {
      label: space.isMuted ? 'Uključi zvuk razgovora' : 'Utišaj razgovor',
      hint: space.isMuted ? '' : 'samo izravna spominjanja',
      onClick: () => api.spacePrefs(space.id, { isMuted: !space.isMuted }).then(refresh(qc)),
    },
  ]
}

export function spaceContextMenu(space: Space, sections: Section[], qc: QueryClient): MenuItem[] {
  return [
    {
      label: 'Označi kao pročitano',
      onClick: () => {
        const t = space.lastActiveTime ?? new Date().toISOString()
        setSpaceRead(qc, space.id, t)
        api.markRead(space.id, t).catch(() => {})
      },
    },
    { label: space.isStarred ? 'Ukloni zvjezdicu' : 'Označi zvjezdicom', onClick: () => api.spacePrefs(space.id, { isStarred: !space.isStarred }).then(refresh(qc)) },
    { label: 'Obavijesti', submenu: notifyMenu(space, qc) },
    {
      label: 'Premjesti u sekciju',
      submenu: [
        ...sections.map((s) => ({
          label: s.name,
          checked: space.sectionId === s.id,
          onClick: () => api.spacePrefs(space.id, { sectionId: s.id }).then(refresh(qc)),
        })),
        ...(space.sectionId ? [{ label: 'Ukloni iz sekcije', onClick: () => api.spacePrefs(space.id, { clearSection: true }).then(refresh(qc)) }] : []),
        { divider: true, label: '' },
        {
          label: 'Nova sekcija…',
          onClick: async () => {
            const name = prompt('Naziv nove sekcije')?.trim()
            if (!name) return
            const s = await api.createSection(name)
            await api.spacePrefs(space.id, { sectionId: s.id })
            qc.invalidateQueries({ queryKey: keys.sections })
            refresh(qc)()
          },
        },
      ],
    },
  ]
}

export function PresenceDot({ state }: { state: 'active' | 'away' | 'offline' }) {
  return <span className={`presence ${state}`} title={{ active: 'Aktivan', away: 'Odsutan', offline: 'Nije povezan' }[state]} />
}
