# API Test Catalog

Stable IDs for test scenarios. Reference them in test names/comments and plan files.

| ID | Area | Scenario | Test |
|---|---|---|---|
| CAT-001 | Events | CloudEvent attributes parsed from Pub/Sub message | WorkspaceEventParserTests.Parses_cloud_event_attributes |
| CAT-002 | Events | Single + batch message payloads | WorkspaceEventParserTests.Extracts_* |
| CAT-003 | Events | Reaction event resolves parent message | WorkspaceEventParserTests.Reaction_resolves_parent_message |
| CAT-010 | Tenancy | Query filter isolates tenants | TenantIsolationTests.Query_filter_hides_other_tenants_data |
| CAT-011 | Tenancy | Cross-tenant writes blocked | TenantIsolationTests.Cross_tenant_write_is_blocked |
| CAT-020 | Sync | Duplicate events deduplicated, edits applied | MessageUpsertTests.Duplicate_events_are_deduplicated_and_updates_applied |
| CAT-021 | Sync | Unknown space skipped | MessageUpsertTests.Messages_for_unknown_spaces_are_skipped |
| CAT-100 | API | (todo) /api/spaces requires auth (WebApplicationFactory) | — |
| CAT-101 | API | (todo) send message validates client id | — |
