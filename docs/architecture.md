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
