# Architecture

## Data flow

1. **Sign-in:** `/auth/login` → Google OAuth (offline + PKCE) → `OnCreatingTicket`:
   - checks `hd` → Tenant
   - upserts AppUser and stores the refresh token (Data Protection)
   - enqueues the initial sync
   - issues the `gs.auth` cookie
2. **Initial sync** (`InitialSyncWorker` → `ChatSyncService.InitialSyncAsync`):
   - spaces.list
   - for the 50 most active spaces: members (DM/group), the last N messages, read state
   - People API for names
   - creates the Workspace Events subscription on `spaces/-`
3. **Real time:** Google → Pub/Sub → `PubSubPullWorker` → `EventProcessor` (subscription → user → tenant) → idempotent upsert in `ChatSyncService` → `IRealtimeNotifier` (SignalR group `space:{id}`).
4. **Reading:** `/api/spaces` and `/api/spaces/{id}/messages` read only from SQL. When the cache runs out, `BackfillOlderAsync` fetches older pages from Google (`Space.HistoryComplete` stops repeated calls).
5. **Sending:** POST `/api/spaces/{id}/messages` with `clientMessageId` (`client-<uuid>`). Google treats it as idempotent. The client inserts the message optimistically and replaces it when the server confirms or the SignalR echo arrives.
6. **Read state:** POST `/read` → local + Google `spaceReadState`, so the official clients show it as read too.

## Notifications (Slack rules)

`NotificationService` runs for every **new** message (deduplicated, younger than 15 min) and for every recipient who is an app user:

1. `NotificationRules.Decide` (pure function in `GoogleSlacking.Core`, unit-tested):
   - Nothing → never
   - a direct @mention beats mute
   - @all / keyword / a reply in a thread I took part in
   - otherwise according to the level: DM/group = All, space = Mentions
2. Mentions, keywords and thread replies are written to **Activity**.
3. DND (pause or schedule in the user's time zone) → stop here (Activity still records it).
4. If the user is looking at that very conversation in a focused window → nothing.
5. SignalR `notification` → open clients show a system notification only if the tab is hidden or on another conversation.
6. **Web Push** only if the user has no active (focused) client. With "Push only when inactive" off: always. The same `tag` (message name) prevents duplicates between SignalR and push.

Presence (`PresenceTracker`): SignalR connections + `Heartbeat(focused, activeSpaceId)` every 30 s. It's in-memory, so for more instances move it to Redis.

## Things Google Chat doesn't have

- **Scheduled messages** and **"remind me"**: `TimersWorker` every 15 s sends due messages as the user (`client-{id}` idempotent) and fires reminders (Activity + push, ignoring DND like Slack).
- **Drafts**: local immediately, to the server after 1.5 s; another device picks up a newer draft.
- **Pins and bookmarks**: shared by all app users in a space, not visible in the official Google Chat.
- **Reactions "who"**: Google only provides counts; we store individual reactions from events, so "your" reaction is highlighted.

## Read receipts (level 1: app users)

Google doesn't expose other people's read state, but we know it for every app user: `SpaceMember.LastReadTime` is updated by our "mark read" and by per-user `spaceReadState` events (read in the official Google Chat as well).

- **Reciprocal opt-in** (`UserSettings.ShareReadReceipts`, off by default): only people who share their own see others', and only for others who also share (`ReadReceiptRules`).
- **Company switch** `Tenant.ReadReceiptsEnabled` (seed: `"ReadReceipts": false` in `Tenants`).
- `GET /api/spaces/{id}/read-receipts` → visible readers + count of "unknown" (they don't share or don't use the app). In a DM where the other person doesn't share, the UI shows nothing (so it doesn't reveal that they opted out).
- The time isn't shown: Google's `lastReadTime` is the time of the last read message, not when it was seen.
- Possible levels 2/3 (all users in the domain): domain-wide delegation + polling, or a customer subscription (Enterprise).

## "… is typing"

- Composer → hub `Typing(spaceId, threadName)` at most every 3 s while there's text (and the user hasn't turned on `HideTyping`).
- The hub checks membership + `HideTyping` (one query) and sends `typing` to `OthersInGroup(space:{id})`. Nothing is stored in the DB.
- The client (`typing.ts`) keeps an entry for 5 s after the last signal and removes it as soon as that person's message arrives. Separately for the main timeline and each thread.
- Shown by default (like Slack); turned off in Settings → Privacy. Only app users see it.
- For manual testing in dev: `__typing(spaceId, null, 'users/123')` in the browser console.

## Multi-tenancy

- `ITenantContext` is scoped. HTTP takes it from the cookie claim `tenant_id`; the SignalR hub sets it per invocation; background workers set it per event or per user.
- Global query filters on every `ITenantOwned` entity. Background code that crosses tenants uses `IgnoreQueryFilters()` explicitly, and only to find the owner (subscription → tenant).
- SignalR groups use globally unique space IDs, so no cross-tenant leaks.

## Scaling notes

- One API instance is enough for an MVP. For more instances:
  - Azure SignalR Service (backplane)
  - Pub/Sub pull in a separate worker (or the GCP Pub/Sub StreamingPull SDK)
  - Data Protection keys already live in the DB
- Quotas: 3000 reads/min per project for all tenants combined; 1 write/s per space. Sending goes through `GoogleHttp` retry/backoff on 429.
