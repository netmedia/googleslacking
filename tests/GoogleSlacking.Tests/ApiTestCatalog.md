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
| CAT-030 | Notifications | DM from someone else notifies recipient | NotificationFlowTests.Dm_from_someone_else_notifies_me |
| CAT-031 | Notifications | Space: no notify without mention; mention notifies + Activity | NotificationFlowTests.Space_message_without_mention_* |
| CAT-032 | Notifications | Own messages never notify | NotificationFlowTests.Own_messages_never_notify |
| CAT-033 | Reactions | Summary counts merged with known users | NotificationFlowTests.Reactions_merge_summary_counts_with_known_users |
| CAT-040 | Core | Web Push encryption = RFC 8291 test vector | WebPushTests.Encryption_matches_rfc8291_test_vector |
| CAT-041 | Core | Notification rules (DM/mention/mute/thread/DND/keywords) | NotificationRulesTests.* |
| CAT-042 | Core | Search syntax parsing | SearchQueryTests.* |
| CAT-043 | Core | Mentions, OpenGraph parsing, SSRF guard | MentionsAndUnfurlTests.* |
| CAT-050 | Read receipts | Reciprocity rules (pure) | ReadReceiptTests.Rules_are_reciprocal |
| CAT-051 | Read receipts | Only sharing members visible to a sharing viewer | ReadReceiptTests.Only_sharing_members_are_visible_to_a_sharing_viewer |
| CAT-052 | Read receipts | Non-sharing viewer sees nothing | ReadReceiptTests.Viewer_who_does_not_share_sees_nothing |
| CAT-053 | Read receipts | Company switch disables | ReadReceiptTests.Company_switch_disables_everything |
