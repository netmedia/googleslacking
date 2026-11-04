# CLAUDE.md — GoogleSlacking

Google Chat client in the style of Slack. .NET 10 backend (BFF + sync) and React PWA. Read `docs/architecture.md` first.

## Workflow
- Branch from `develop`. Feature branches as `feature/<short-name>`; worktrees are welcome.
- Plan mode by default for anything larger than a trivial change.
- Commit messages: one line, with a reference to the plan file if there is one.
- `.claude/settings.local.json` is not committed.

## Commands
- Build: `dotnet build GoogleSlacking.slnx`
- Tests: `dotnet test` (xUnit + SQLite in-memory). New scenarios go in `tests/GoogleSlacking.Tests/ApiTestCatalog.md` with a stable `CAT-XXX` ID.
- Migration: `dotnet ef migrations add <Name> -p src/GoogleSlacking.Infrastructure -s src/GoogleSlacking.Api`
- Web: `cd web && npm run dev` / `npm run typecheck` / `npm run build:api`

## Rules
- **Secrets never in the repo or in files Claude reads:** user secrets (shared UserSecretsId in `Directory.Build.props`), `appsettings.Local.json` (gitignored), service-account JSON outside the repo.
- **Tenancy:** every new entity that belongs to a customer implements `ITenantOwned` and gets `HasQueryFilter` in `AppDbContext`. `IgnoreQueryFilters()` only in background code, with a comment on why.
- **Google API calls only through `GoogleSlacking.Google` clients** (retry/backoff on 429). Read endpoints don't call Google, except the `BackfillOlderAsync` fallback.
- Event processing must be **idempotent**: the same event arrives once for each subscribed member.
- Chat API with user auth doesn't return sender names; use `Person` / `ResolvePeopleAsync`.
- UI text is in Croatian.
