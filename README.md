# GoogleSlacking

A fast Google Chat client in the style of Slack (PWA, then desktop and mobile). It's an internal Netmedia tool that other companies can run as their own internal tool.

```
Google Chat API ──┐
Workspace Events API → Pub/Sub → PubSubPullWorker ─→ SQL cache (multi-tenant)
                                                  ├→ SignalR /hubs/chat → clients
                                                  └→ (todo) Web Push / FCM / APNs
ASP.NET Core (BFF): Google OAuth, refresh tokens (Data Protection), REST /api, serves the PWA
React PWA: IndexedDB cache → instant start, optimistic sending, Ctrl+K, threads
```

## Structure

| Folder | What it contains |
|---|---|
| `src/GoogleSlacking.Domain` | Entities (Tenant, AppUser, Space, SpaceMember, Message, Person, EventSubscription) |
| `src/GoogleSlacking.Google` | Hand-written REST clients: Chat, Workspace Events, People, Pub/Sub, OAuth. No Google SDK dependency |
| `src/GoogleSlacking.Infrastructure` | EF Core (SQL Server), tenant filter, token store, sync, event processing, background workers |
| `src/GoogleSlacking.Api` | Minimal API, Google sign-in, SignalR hub, SPA hosting |
| `tests/GoogleSlacking.Tests` | xUnit + SQLite in-memory; see `ApiTestCatalog.md` |
| `web/` | Vite + React + TS PWA |
| `docs/` | `google-setup.md` (GCP/Workspace setup), `architecture.md` |

## Running locally

Prerequisites: .NET 10 SDK, Node 22+, SQL Server LocalDB, and a configured GCP project (see [docs/google-setup.md](docs/google-setup.md)).

```powershell
# 1. Secrets (shared UserSecretsId for the whole solution; they live outside the repo)
cd src/GoogleSlacking.Api
dotnet user-secrets set "Google:ClientId" "<client-id>.apps.googleusercontent.com"
dotnet user-secrets set "Google:ClientSecret" "<secret>"
dotnet user-secrets set "Google:ProjectId" "<gcp-project-id>"
dotnet user-secrets set "Google:ServiceAccountKeyPath" "C:\\secrets\\googleslacking-pubsub.json"

# 2. Database (the first migration)
dotnet tool install -g dotnet-ef   # if you don't have it
cd ../..
dotnet ef migrations add Initial -p src/GoogleSlacking.Infrastructure -s src/GoogleSlacking.Api

# 3. API (http://localhost:5080). In Development it migrates the DB itself and seeds tenants from appsettings.Development.json
dotnet run --project src/GoogleSlacking.Api

# 4. Web (http://localhost:5173, proxies /api, /auth and /hubs to the API)
cd web
npm install
npm run dev
```

Open http://localhost:5173, sign in, and the first sync starts in the background. With no Pub/Sub set up, set `Sync:EnablePubSub=false`. Everything works except real-time updates.

Production: `npm run build:api` builds the PWA into `src/GoogleSlacking.Api/wwwroot`, and the API serves everything from one origin.

## Key decisions

- **Tenant = Workspace domain** (the OIDC `hd` claim). An EF global query filter on every `ITenantOwned` entity, and `SaveChanges` blocks cross-tenant writes.
- **Reads come from the local cache.** Google is called only for backfill ("load older"), sending, and read state. The project quota is 3000 message reads per minute for all tenants combined.
- **One subscription per user** on `//chat.googleapis.com/spaces/-`. The same event arrives once per member, so upsert is idempotent and only real changes are broadcast.
- **Subscriptions with resource data expire within a few hours.** `SubscriptionMaintenanceWorker` renews them, and expirationReminder events trigger renewal too.
- **Sender names:** with user auth the Chat API doesn't return `displayName`, so the People API resolves them (`directory.readonly`) and they're cached in `People`.
- **Refresh tokens** are protected with ASP.NET Data Protection. The key ring is in the DB; for production add Key Vault protection.

## TODO (next steps)

- [ ] Push notifications (Web Push for the PWA; FCM/APNs for Capacitor) and per-device settings
- [ ] User-level subscription (read state from other clients → unread sync)
- [ ] Full-text search (SQL Server FTS over `Messages.Text`)
- [ ] Attachments (download/upload via Chat media API), mentions, emoji reactions from the client
- [ ] Starred/muted spaces (UI + endpoint), sections
- [ ] Tauri (desktop) and Capacitor (mobile) wrappers
- [ ] Self-hosted package: Dockerfile + Bicep (Azure Container Apps + Azure SQL + Key Vault)
- [ ] Pub/Sub dead-letter topic, health checks, OpenTelemetry
