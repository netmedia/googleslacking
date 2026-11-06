// Regenerates docs/screenshots with mocked API data (no backend needed).
// Usage: cd web && NODE_ENV=development npx vite build --mode development && npx vite preview --port 4173
//        (dev build: exposes window.__typing for the "… tipka" screenshot; then in another terminal)
//        npm i -D playwright && npx playwright install chromium && node scripts/screenshots.cjs
const { chromium } = require('playwright')
const path = require('path')

const out = path.resolve(__dirname, '../../docs/screenshots') + '/'
const BASE = 'http://localhost:4173'
const now = Date.now()
const t = (minAgo) => new Date(now - minAgo * 60000).toISOString()
const id = (n) => `${n}${n}${n}${n}${n}${n}${n}${n}-0000-0000-0000-000000000000`
const sec1 = 'aaaaaaaa-0000-0000-0000-000000000001'

const S = (sid, name, kind, extra = {}) => ({
  id: sid, googleName: 'spaces/' + sid.slice(0, 4), displayName: name, kind, isThreaded: false, lastActiveTime: t(1),
  lastReadTime: t(100000), unreadCount: 0, mentionCount: 0, isStarred: false, isMuted: false, notifyLevel: 0,
  sectionId: null, sortOrder: 0, dmUserName: null, ...extra,
})
const spaces = [
  S(id(1), 'dev-team', 0, { lastReadTime: t(50) }),
  S(id(2), 'ERP2024', 0, { isStarred: true, isThreaded: true, unreadCount: 2 }),
  S(id(3), 'general', 0, { unreadCount: 5 }),
  S(id(4), 'deploys', 0, { isMuted: true, unreadCount: 12 }),
  S(id(5), 'design-review', 0, { mentionCount: 1, unreadCount: 3 }),
  S(id(9), 'hasek-integration', 0, { sectionId: sec1 }),
  S(id(6), 'Marko Kovač', 2, { unreadCount: 1, dmUserName: 'users/2' }),
  S(id(7), 'Ivana Horvat', 2, { dmUserName: 'users/3' }),
  S(id(8), 'Petra Babić, Luka Jurić', 1),
]
const people = { 1: 'Vladan', 2: 'Marko Kovač', 3: 'Ivana Horvat', 4: 'Luka Jurić', 5: 'Petra Babić' }
let n = 0
const M = (spaceId, who, min, text, extra = {}) => ({
  id: `00000000-0000-0000-0000-${String(++n).padStart(12, '0')}`, spaceId, googleName: `spaces/x/messages/${n}`, threadName: `spaces/x/threads/${n}`,
  isThreadReply: false, sender: { id: 'users/' + who, displayName: people[who], avatarUrl: null, email: null }, text, formattedText: null,
  reactions: [], attachments: null, annotations: null, createTime: t(min), lastUpdateTime: null, isDeleted: false, isPinned: false,
  clientAssignedMessageId: null, ...extra,
})
const newest = (arr) => [...arr].sort((a, b) => b.createTime.localeCompare(a.createTime))

const pinned = M(id(1), 3, 1500, 'Sprint planning svaki ponedjeljak u 10h — agenda je u Jiri.', { isPinned: true })
const mention = M(id(1), 2, 30, '@Vladan možeš li baciti oko na ovo prije deploya?', {
  annotations: [{ type: 'USER_MENTION', startIndex: 0, length: 7, userMention: { user: { name: 'users/1' } } }],
})
const dev = newest([
  pinned,
  M(id(1), 2, 1497, 'Može, dolazim.'),
  M(id(1), 4, 62, 'PR za novi checkout flow je spreman za review: https://github.com/netmedia/shop/pull/412'),
  M(id(1), 4, 61, 'Dodao sam i *integracijske testove* za plaćanje.', { reactions: [{ emoji: '🚀', count: 3, users: ['users/1', 'users/2'] }, { emoji: '👀', count: 1, users: ['users/3'] }] }),
  M(id(1), 1, 55, 'Gledam sad. Jedna stvar — `PaymentService` bi trebao imati retry na 429.'),
  mention,
  M(id(1), 4, 25, '> trebao imati retry na 429\nDodano, exponential backoff s jitterom.', { lastUpdateTime: t(24) }),
  M(id(1), 2, 12, 'Staging je ažuriran 👇\nhttps://staging.netmedia.dev', { reactions: [{ emoji: '👍', count: 2, users: ['users/1'] }, { emoji: '🎉', count: 1, users: [] }] }),
  M(id(1), 3, 8, 'Screenshot novog checkouta:', {
    attachments: [{ contentName: 'checkout.png', contentType: 'image/png', attachmentDataRef: { resourceName: 'x' } }, { contentName: 'specifikacija-v3.pdf', contentType: 'application/pdf', attachmentDataRef: { resourceName: 'y' } }],
  }),
])
const th = 'spaces/x/threads/erp'
const erpRoot = M(id(2), 5, 90, 'Klijent traži export ponuda u Excel s grupiranjem po destinaciji. Ideje?', { threadName: th })
const erpReplies = [
  M(id(2), 1, 85, 'Možemo preko postojećeg report endpointa, samo dodati GROUP BY i ClosedXML.', { threadName: th, isThreadReply: true }),
  M(id(2), 2, 80, 'Slažem se. Imam već helper za stilove tablica.', { threadName: th, isThreadReply: true }),
  M(id(2), 5, 20, 'Super, javljam klijentu da je u sljedećem releaseu.', { threadName: th, isThreadReply: true, reactions: [{ emoji: '🙌', count: 2, users: ['users/1'] }] }),
]
const erp = newest([M(id(2), 3, 300, 'Release 2.14 je na produkciji.'), erpRoot, ...erpReplies])

const dm = newest([
  M(id(6), 2, 40, 'Hej, imaš 5 min za brzi call oko Hašek integracije?'),
  M(id(6), 1, 38, 'Može za 10 min, završavam review.'),
  M(id(6), 2, 37, 'Super 👍'),
  M(id(6), 1, 2, 'Evo me, šaljem Meet link.'),
])
const settings = { keywords: 'deploy, produkcija', timeZone: 'Europe/Zagreb', dndScheduleEnabled: true, dndStart: '22:00:00', dndEnd: '08:00:00', snoozeUntil: null, notifyThreadReplies: true, pushOnlyWhenInactive: true, statusEmoji: '🤿', statusText: 'Ronim do 14h', statusExpiresAt: null, forceAway: false, shareReadReceipts: true, readReceiptsAvailable: true }
const presence = [
  { userName: 'users/1', state: 'active', statusEmoji: '🤿', statusText: 'Ronim do 14h' },
  { userName: 'users/2', state: 'active', statusEmoji: null, statusText: null },
  { userName: 'users/3', state: 'away', statusEmoji: '📅', statusText: 'Na sastanku' },
]
const activity = {
  unread: 2,
  items: [
    { id: 'a1', kind: 0, spaceId: id(1), spaceName: 'dev-team', message: mention, actor: mention.sender, preview: mention.text, createdAt: t(30), isRead: false },
    { id: 'a2', kind: 2, spaceId: id(2), spaceName: 'ERP2024', message: erpReplies[2], actor: erpReplies[2].sender, preview: erpReplies[2].text, createdAt: t(20), isRead: false },
    { id: 'a3', kind: 4, spaceId: id(1), spaceName: 'dev-team', message: dev.find((m) => m.text?.startsWith('Gledam')), actor: { id: 'users/4', displayName: 'Luka Jurić', avatarUrl: null, email: null }, preview: '👍', createdAt: t(50), isRead: true },
  ],
}
const saved = [{ id: 's1', message: dev.find((m) => m.text?.startsWith('PR za')), spaceName: 'dev-team', createdAt: t(60), remindAt: new Date(now + 3 * 3600_000).toISOString(), completedAt: null }]
const scheduled = [{ id: 'sc1', spaceId: id(3), spaceName: 'general', threadName: null, text: 'Podsjetnik: sutra u 10h demo za klijenta 🎯', sendAt: new Date(now + 20 * 3600_000).toISOString(), status: 0, error: null }]
const members = Object.entries(people).map(([k, v]) => ({ id: 'users/' + k, displayName: v, avatarUrl: null, email: v.split(' ')[0].toLowerCase() + '@netmedia.hr', isAppUser: k === '1' || k === '2' }))

async function page(browser, opts = {}) {
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 820 }, deviceScaleFactor: 2, timezoneId: 'Europe/Zagreb', locale: 'hr-HR', ...opts })
  const p = await ctx.newPage()
  const json = (data) => (r) => r.fulfill({ json: data })
  await p.route('**/api/me', json({ id: 'x', googleUserName: 'users/1', email: 'vladan@netmedia.hr', displayName: 'Vladan', avatarUrl: null, syncCompleted: true }))
  await p.route('**/api/spaces', json(spaces))
  await p.route('**/api/settings', json(settings))
  await p.route('**/api/presence', json(presence))
  await p.route('**/api/sections', json([{ id: sec1, name: 'Klijenti', sortOrder: 0, isCollapsed: false }]))
  await p.route('**/api/activity', json(activity))
  await p.route('**/api/saved*', json(saved))
  await p.route('**/api/scheduled', json(scheduled))
  await p.route('**/api/drafts', json([{ spaceId: id(5), threadName: null, text: 'Mislim da bi gumb trebao biti…', updatedAt: t(5) }]))
  await p.route('**/api/push/key', json({ publicKey: 'x' }))
  await p.route('**/api/spaces/*/members', json(members))
  await p.route('**/api/spaces/*/pins', json([pinned]))
  await p.route('**/api/spaces/*/bookmarks', json([{ id: 'b1', title: 'Jira board', url: 'https://jira.example.com', createdAt: t(1) }, { id: 'b2', title: 'Staging', url: 'https://staging.netmedia.dev', createdAt: t(1) }]))
  await p.route('**/api/unfurl*', json({ url: 'https://github.com/netmedia/shop/pull/412', title: 'Checkout flow v2 by lukajuric · Pull Request #412', description: 'Novi checkout s Apple/Google Pay, retry logikom i integracijskim testovima.', imageUrl: null, siteName: 'GitHub' }))
  await p.route('**/api/search*', json(dev.filter((m) => m.text?.includes('retry') || m.text?.includes('deploy')).map((m) => ({ message: m, spaceName: 'dev-team' }))))
  await p.route('**/api/messages/*/attachments/0', (r) => r.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="480" height="260"><rect width="480" height="260" fill="#eef3f8"/><rect x="40" y="40" width="400" height="44" rx="8" fill="#fff" stroke="#cbd5e1"/><rect x="40" y="104" width="400" height="44" rx="8" fill="#fff" stroke="#cbd5e1"/><rect x="40" y="180" width="180" height="44" rx="8" fill="#007a5a"/><text x="130" y="208" font-family="sans-serif" font-size="16" fill="#fff" text-anchor="middle">Plati</text></svg>' }))
  await p.route('**/api/spaces/*/messages*', (r) => {
    const u = new URL(r.request().url())
    if (u.searchParams.get('thread')) return r.fulfill({ json: newest([erpRoot, ...erpReplies]) })
    if (u.pathname.includes(id(6))) return r.fulfill({ json: dm })
    return r.fulfill({ json: u.pathname.includes(id(2)) ? erp : dev })
  })
  await p.route('**/api/spaces/*/read', (r) => r.fulfill({ status: 204 }))
  await p.route('**/api/spaces/*/read-receipts', (r) =>
    r.fulfill({
      json: r.request().url().includes(id(6))
        ? { enabled: true, readers: [{ userName: 'users/2', lastReadTime: t(0) }], unknownCount: 0 }
        : { enabled: true, readers: [{ userName: 'users/2', lastReadTime: t(0) }, { userName: 'users/3', lastReadTime: t(0) }, { userName: 'users/4', lastReadTime: t(120) }], unknownCount: 1 },
    }),
  )
  await p.route('**/hubs/**', (r) => r.abort())
  await p.addInitScript(() => {
    window.Notification = { permission: 'granted', requestPermission: async () => 'granted' }
  })
  return p
}

// The mocked backend has no SignalR hub: show the "connected" state in screenshots.
const fixStatus = (p) =>
  p.evaluate(() =>
    document.querySelectorAll('.status.connecting, .status.offline').forEach((s) => {
      s.className = 'status online'
      s.textContent = 'Ronim do 14h'
    }),
  )

;(async () => {
  const b = await chromium.launch()
  let p = await page(b)
  await p.goto(`${BASE}/#/space/${id(1)}`)
  await p.waitForTimeout(1500)
  await fixStatus(p)
  await p.hover('text=Dodano, exponential backoff')
  await fixStatus(p)
  await p.screenshot({ path: out + 'channel.png' })

  await p.evaluate((sid) => {
    window.__typing?.(sid, null, 'users/2')
    window.__typing?.(sid, null, 'users/5')
  }, id(1))
  await p.waitForTimeout(400)
  await fixStatus(p)
  await p.screenshot({ path: out + 'typing.png' })
  await p.waitForTimeout(5500) // let the typing indicator expire for the next screenshots

  await p.evaluate((sid) => (location.hash = `#/space/${sid}`), id(6))
  await p.waitForTimeout(900)
  await fixStatus(p)
  await p.screenshot({ path: out + 'read-receipts-dm.png' })

  await p.evaluate((sid) => (location.hash = `#/space/${sid}/t/${encodeURIComponent('spaces/x/threads/erp')}`), id(2))
  await p.waitForTimeout(1000)
  await fixStatus(p)
  await p.screenshot({ path: out + 'thread.png' })

  await p.evaluate((sid) => (location.hash = `#/space/${sid}`), id(1))
  await p.waitForTimeout(500)
  await p.click('.sidebar-nav button:has-text("Aktivnost")')
  await p.waitForTimeout(700)
  await fixStatus(p)
  await p.screenshot({ path: out + 'activity.png' })

  await p.click('.sidebar-nav button:has-text("Kasnije")')
  await p.waitForTimeout(700)
  await fixStatus(p)
  await p.screenshot({ path: out + 'later.png' })

  await p.keyboard.press('Control+g')
  await p.waitForTimeout(300)
  await p.fill('.panel-search > input', 'retry in:#dev-team')
  await p.waitForTimeout(900)
  await fixStatus(p)
  await p.screenshot({ path: out + 'search.png' })

  await p.click('.side-panel .channel-header button[aria-label="Zatvori"]')
  await p.hover('.msg:has-text("Dodano, exponential")')
  await p.click('.msg:has-text("Dodano, exponential") .msg-actions button[title="Više"]')
  await p.waitForTimeout(300)
  await fixStatus(p)
  await p.screenshot({ path: out + 'message-menu.png' })
  await p.keyboard.press('Escape')

  await p.click('.composer textarea')
  await p.keyboard.type('Hvala @Ma')
  await p.waitForTimeout(500)
  await fixStatus(p)
  await p.screenshot({ path: out + 'mentions.png' })
  await p.fill('.composer textarea', '')

  await p.keyboard.press('Control+k')
  await p.keyboard.type('mar')
  await p.waitForTimeout(300)
  await fixStatus(p)
  await p.screenshot({ path: out + 'quick-switcher.png' })
  await p.keyboard.press('Escape')

  await p.click('.me-button')
  await p.click('text=Postavke')
  await p.waitForTimeout(500)
  await fixStatus(p)
  await p.screenshot({ path: out + 'settings.png' })
  await p.click('.modal .tabs button:has-text("Privatnost")')
  await p.waitForTimeout(200)
  await fixStatus(p)
  await p.screenshot({ path: out + 'settings-privacy.png' })
  await p.context().close()

  p = await page(b, { colorScheme: 'dark' })
  await p.goto(`${BASE}/#/space/${id(1)}`)
  await p.waitForTimeout(1500)
  await fixStatus(p)
  await fixStatus(p)
  await p.screenshot({ path: out + 'channel-dark.png' })
  await p.context().close()

  p = await page(b, { viewport: { width: 390, height: 780 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true })
  await p.goto(`${BASE}/`)
  await p.waitForTimeout(1200)
  await fixStatus(p)
  await fixStatus(p)
  await p.screenshot({ path: out + 'mobile-list.png' })
  await p.click('.space:has-text("dev-team")')
  await p.waitForTimeout(1000)
  await fixStatus(p)
  await p.screenshot({ path: out + 'mobile-channel.png' })
  await p.context().close()

  const ctx = await b.newContext({ viewport: { width: 1280, height: 760 }, deviceScaleFactor: 2 })
  p = await ctx.newPage()
  await p.route('**/api/me', (r) => r.fulfill({ status: 401 }))
  await p.goto(`${BASE}/`)
  await p.waitForTimeout(800)
  await fixStatus(p)
  await p.screenshot({ path: out + 'login.png' })
  await b.close()
})()
