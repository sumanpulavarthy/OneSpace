# FR-4 Query logging

**Status:** Implemented

Each question writes one `queryLogs` document: user id, session, question, resolved intent, systems called, every tool call (including **blocked** attempts with the reason), a 500-character response summary, latency and failure flag.

- Written even when the request fails or is cancelled; a logging failure never fails the answer.
- Employees can read only their own log (`recentQueries`).
- 30-day TTL (`Database:QueryLogRetentionDays`).
- **Not** the compliance audit trail. Section 6 requires an immutable audit layer before Phase 1 adds PazCare (Open Item 4).
- App Insights receives request telemetry when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
