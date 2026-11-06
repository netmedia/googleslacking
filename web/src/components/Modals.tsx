import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, type Member, type Settings } from '../api'
import { keys } from '../cache'
import { useSettings } from '../hooks'
import { disablePush, enablePush, getPushState, type PushState } from '../push'
import { setModal, toast } from '../store'
import { fmtWhen, snoozePresets } from '../time'
import { EmojiPicker } from './EmojiPicker'
import { PersonPicker } from './PersonPicker'
import { Modal, Popover } from './ui'

// ---------------- Settings ----------------
const pushText: Record<PushState, string> = {
  unsupported: 'Ovaj preglednik ne podržava push obavijesti. Na iPhoneu dodaj aplikaciju na početni zaslon (Share → Add to Home Screen).',
  unconfigured: 'Push nije konfiguriran na serveru (VAPID ključevi).',
  denied: 'Obavijesti su blokirane u pregledniku. Dozvoli ih u postavkama stranice.',
  enabled: 'Uključeno na ovom uređaju.',
  disabled: 'Isključeno na ovom uređaju.',
}

export function SettingsModal() {
  const qc = useQueryClient()
  const current = useSettings().data
  const [s, setS] = useState<Settings | null>(null)
  const [tab, setTab] = useState<'notify' | 'dnd' | 'status' | 'privacy'>('notify')
  const [push, setPush] = useState<PushState | null>(null)
  const [emojiAnchor, setEmojiAnchor] = useState<HTMLElement | null>(null)

  useEffect(() => {
    if (current && !s) setS(current)
  }, [current, s])
  useEffect(() => {
    getPushState().then(setPush)
  }, [])

  if (!s) return null
  const set = <K extends keyof Settings>(k: K, v: Settings[K]) => setS({ ...s, [k]: v })

  const save = async () => {
    await api.saveSettings(s)
    qc.invalidateQueries({ queryKey: keys.settings })
    qc.invalidateQueries({ queryKey: keys.presence })
    toast('Postavke spremljene.')
    setModal(null)
  }

  const snoozed = s.snoozeUntil && Date.parse(s.snoozeUntil) > Date.now()
  const hhmm = (t: string | null) => (t ? t.slice(0, 5) : '')

  return (
    <Modal title="Postavke" onClose={() => setModal(null)} wide>
      <div className="tabs">
        <button className={tab === 'notify' ? 'on' : ''} onClick={() => setTab('notify')}>
          Obavijesti
        </button>
        <button className={tab === 'dnd' ? 'on' : ''} onClick={() => setTab('dnd')}>
          Ne ometaj
        </button>
        <button className={tab === 'status' ? 'on' : ''} onClick={() => setTab('status')}>
          Status
        </button>
        <button className={tab === 'privacy' ? 'on' : ''} onClick={() => setTab('privacy')}>
          Privatnost
        </button>
      </div>

      {tab === 'privacy' && (
        <div className="form">
          <fieldset>
            <legend>Potvrde čitanja</legend>
            {s.readReceiptsAvailable ? (
              <>
                <label className="check">
                  <input type="checkbox" checked={s.shareReadReceipts} onChange={(e) => set('shareReadReceipts', e.target.checked)} />
                  Dijeli kada sam pročitao/la poruke
                </label>
                <p className="muted small">
                  Uzajamno, kao na WhatsAppu: drugi vide kada si pročitao/la njihove poruke, a ti vidiš njihove — ali samo kod osoba koje su
                  također uključile dijeljenje i koriste ovu aplikaciju. Ako isključiš, ne vidiš ni tuđe potvrde. Službeni Google Chat potvrde ne
                  prikazuje.
                </p>
              </>
            ) : (
              <p className="muted">Potvrde čitanja isključene su za cijelu tvrtku.</p>
            )}
          </fieldset>
          <fieldset>
            <legend>Tipkanje</legend>
            <label className="check">
              <input type="checkbox" checked={s.hideTyping} onChange={(e) => set('hideTyping', e.target.checked)} />
              Ne prikazuj drugima da tipkam
            </label>
            <p className="muted small">„… tipka” vide samo korisnici ove aplikacije u istom razgovoru. Službeni Google Chat ga ne prikazuje.</p>
          </fieldset>
        </div>
      )}

      {tab === 'notify' && (
        <div className="form">
          <fieldset>
            <legend>Push obavijesti na ovom uređaju</legend>
            <p className="muted">{push ? pushText[push] : '…'}</p>
            <div className="row">
              {push === 'disabled' && (
                <button className="btn primary" onClick={() => enablePush().then(setPush, () => toast('Uključivanje nije uspjelo.', 'error'))}>
                  Uključi push obavijesti
                </button>
              )}
              {push === 'enabled' && (
                <>
                  <button className="btn" onClick={() => api.pushTest().then((r) => toast(r.delivered ? 'Testna obavijest poslana.' : 'Nijedan uređaj nije primio obavijest.'))}>
                    Pošalji testnu
                  </button>
                  <button className="btn" onClick={() => disablePush().then(setPush)}>
                    Isključi
                  </button>
                </>
              )}
            </div>
          </fieldset>
          <label className="check">
            <input type="checkbox" checked={s.pushOnlyWhenInactive} onChange={(e) => set('pushOnlyWhenInactive', e.target.checked)} />
            Šalji push na mobitel samo kad nisam aktivan na računalu
          </label>
          <label className="check">
            <input type="checkbox" checked={s.notifyThreadReplies} onChange={(e) => set('notifyThreadReplies', e.target.checked)} />
            Obavijesti me o odgovorima u nitima u kojima sudjelujem
          </label>
          <label>
            Ključne riječi <span className="muted">(odvojene zarezom — obavještavaju kao spominjanje)</span>
            <input value={s.keywords ?? ''} placeholder="npr. deploy, produkcija, Vladan" onChange={(e) => set('keywords', e.target.value)} />
          </label>
          <p className="muted small">
            Po razgovoru: 🔔 u zaglavlju razgovora ili desni klik u bočnoj traci. Zadano: direktne poruke — sve; prostori — samo spominjanja.
          </p>
        </div>
      )}

      {tab === 'dnd' && (
        <div className="form">
          <fieldset>
            <legend>Pauziraj obavijesti</legend>
            {snoozed ? (
              <div className="row">
                <span>Pauzirano do {fmtWhen(s.snoozeUntil!)}</span>
                <button className="btn" onClick={() => api.snooze(null).then((r) => setS({ ...s, snoozeUntil: r.snoozeUntil }))}>
                  Nastavi obavijesti
                </button>
              </div>
            ) : (
              <div className="row wrap">
                {snoozePresets().map((p) => (
                  <button key={p.label} className="btn" onClick={() => api.snooze(p.minutes).then((r) => setS({ ...s, snoozeUntil: r.snoozeUntil }))}>
                    {p.label}
                  </button>
                ))}
              </div>
            )}
          </fieldset>
          <fieldset>
            <legend>Raspored „Ne ometaj”</legend>
            <label className="check">
              <input type="checkbox" checked={s.dndScheduleEnabled} onChange={(e) => set('dndScheduleEnabled', e.target.checked)} />
              Automatski isključi obavijesti
            </label>
            <div className="row">
              <label>
                Od <input type="time" value={hhmm(s.dndStart) || '22:00'} onChange={(e) => set('dndStart', e.target.value + ':00')} />
              </label>
              <label>
                Do <input type="time" value={hhmm(s.dndEnd) || '08:00'} onChange={(e) => set('dndEnd', e.target.value + ':00')} />
              </label>
              <label>
                Vremenska zona <input value={s.timeZone} onChange={(e) => set('timeZone', e.target.value)} />
              </label>
            </div>
          </fieldset>
        </div>
      )}

      {tab === 'status' && (
        <div className="form">
          <div className="row">
            <button className="btn emoji-btn" onClick={(e) => setEmojiAnchor(e.currentTarget)}>
              {s.statusEmoji ?? '🙂'}
            </button>
            <input className="grow" value={s.statusText ?? ''} placeholder="Što radiš? npr. Na sastanku, Na godišnjem…" onChange={(e) => set('statusText', e.target.value)} />
          </div>
          <div className="row wrap">
            {[
              ['📅', 'Na sastanku', 60],
              ['🚗', 'Na putu', 120],
              ['🤒', 'Bolovanje', 60 * 24],
              ['🏖️', 'Na godišnjem', 60 * 24 * 7],
              ['🏠', 'Radim od kuće', 60 * 10],
            ].map(([e, t, min]) => (
              <button
                key={t as string}
                className="chip"
                onClick={() => setS({ ...s, statusEmoji: e as string, statusText: t as string, statusExpiresAt: new Date(Date.now() + (min as number) * 60_000).toISOString() })}
              >
                {e} {t}
              </button>
            ))}
          </div>
          <label>
            Obriši nakon
            <select
              value=""
              onChange={(e) => {
                const min = Number(e.target.value)
                set('statusExpiresAt', min ? new Date(Date.now() + min * 60_000).toISOString() : null)
              }}
            >
              <option value="">{s.statusExpiresAt ? fmtWhen(s.statusExpiresAt) : 'Nikad'}</option>
              <option value="30">30 minuta</option>
              <option value="60">1 sat</option>
              <option value="240">4 sata</option>
              <option value={Math.round((new Date().setHours(23, 59, 0, 0) - Date.now()) / 60_000)}>Danas</option>
              <option value="0">Nikad</option>
            </select>
          </label>
          <label className="check">
            <input type="checkbox" checked={s.forceAway} onChange={(e) => set('forceAway', e.target.checked)} />
            Prikaži me kao odsutnog
          </label>
          {(s.statusText || s.statusEmoji) && (
            <button className="link" onClick={() => setS({ ...s, statusText: null, statusEmoji: null, statusExpiresAt: null })}>
              Obriši status
            </button>
          )}
          {emojiAnchor && (
            <Popover anchor={emojiAnchor} onClose={() => setEmojiAnchor(null)}>
              <EmojiPicker
                onPick={(e) => {
                  setEmojiAnchor(null)
                  set('statusEmoji', e)
                }}
              />
            </Popover>
          )}
        </div>
      )}

      <footer className="modal-footer">
        <button className="btn" onClick={() => setModal(null)}>
          Odustani
        </button>
        <button className="btn primary" onClick={save}>
          Spremi
        </button>
      </footer>
    </Modal>
  )
}

// ---------------- New conversation ----------------
export function NewConversationModal({ onCreated }: { onCreated: (spaceId: string) => void }) {
  const qc = useQueryClient()
  const [kind, setKind] = useState<'people' | 'space'>('people')
  const [people, setPeople] = useState<Member[]>([])
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)

  const create = async () => {
    setBusy(true)
    try {
      const ids = people.map((p) => p.id)
      const r =
        kind === 'space'
          ? await api.createSpace('space', ids, name.trim())
          : await api.createSpace(ids.length === 1 ? 'dm' : 'group', ids)
      await qc.invalidateQueries({ queryKey: keys.spaces })
      setModal(null)
      onCreated(r.id)
    } catch {
      toast('Stvaranje razgovora nije uspjelo.', 'error')
    } finally {
      setBusy(false)
    }
  }

  const valid = kind === 'space' ? name.trim().length > 0 : people.length > 0
  return (
    <Modal title="Novi razgovor" onClose={() => setModal(null)}>
      <div className="tabs">
        <button className={kind === 'people' ? 'on' : ''} onClick={() => setKind('people')}>
          Poruka osobi / grupi
        </button>
        <button className={kind === 'space' ? 'on' : ''} onClick={() => setKind('space')}>
          Novi prostor
        </button>
      </div>
      <div className="form">
        {kind === 'space' && (
          <label>
            Naziv prostora
            <input autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder="npr. projekt-hasek" />
          </label>
        )}
        <label>
          {kind === 'space' ? 'Članovi (možeš i kasnije)' : 'Kome?'}
          <PersonPicker selected={people} onChange={setPeople} autoFocus={kind === 'people'} />
        </label>
        {kind === 'people' && people.length > 1 && <p className="muted small">Stvorit će se grupni razgovor s {people.length} osobe.</p>}
      </div>
      <footer className="modal-footer">
        <button className="btn" onClick={() => setModal(null)}>
          Odustani
        </button>
        <button className="btn primary" disabled={!valid || busy} onClick={create}>
          {kind === 'space' ? 'Stvori prostor' : people.length > 1 ? 'Stvori grupu' : 'Otvori razgovor'}
        </button>
      </footer>
    </Modal>
  )
}

// ---------------- Shortcuts ----------------
const SHORTCUTS: [string, string][] = [
  ['Ctrl+K', 'Brzi prelazak na razgovor'],
  ['Ctrl+G', 'Pretraga'],
  ['Alt+↑ / Alt+↓', 'Prethodni / sljedeći razgovor'],
  ['Alt+Shift+↑ / ↓', 'Prethodni / sljedeći nepročitani'],
  ['Esc', 'Označi razgovor pročitanim'],
  ['Shift+Esc', 'Označi sve pročitanim'],
  ['↑ (prazan unos)', 'Uredi zadnju poruku'],
  ['Enter / Shift+Enter', 'Pošalji / novi red'],
  ['Ctrl+B / Ctrl+I / Ctrl+Shift+X', 'Podebljano / kurziv / precrtano'],
  ['@', 'Spomeni osobu'],
  ['Ctrl+Shift+A', 'Aktivnost'],
  ['Ctrl+Shift+L', 'Kasnije'],
  ['Ctrl+N', 'Novi razgovor'],
  ['Ctrl+/', 'Ovaj popis'],
]

export function ShortcutsModal() {
  return (
    <Modal title="Tipkovničke prečice" onClose={() => setModal(null)}>
      <table className="shortcuts">
        <tbody>
          {SHORTCUTS.map(([k, d]) => (
            <tr key={k}>
              <td>
                <kbd>{k}</kbd>
              </td>
              <td>{d}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Modal>
  )
}
