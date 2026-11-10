# Product Requirement Document (PRD)
## OneSpace — Digital Front Door to the Workplace

### 📑 Document Control
* Project Name: OneSpace
* Version: 0.2 (Reconciled — Phased Scope)
* Status: Draft for Review
* Submission: BuildX
* Team: 2 developers, 2 QAs

---

## 1. Problem Statement
Employees juggle 6–10 disconnected tools — Zoho People for HR, PazCare for benefits, ComplyKaro for compliance, Outlook/Teams for communication, plus separate portals for helpdesk, recognition, and events. Finding a simple answer (leave balance, reimbursement status, a policy document) means hopping across systems, remembering logins, and often just asking a colleague or HR directly.

This fragmentation costs time, buries information, and quietly erodes engagement — employees disengage from programs (recognition, wellness, idea submissions) simply because they're hard to find.

## 2. Solution
OneSpace is a unified employee experience platform that brings workplace services into a single, AI-searchable hub — integrated with existing systems (Zoho People, PazCare, ComplyKaro, M365/Teams/Outlook) rather than replacing them.

**Core promise:** Turn a 6-app, 15-minute HR errand into a 30-second conversational query.

**One-line pitch:** "OneSpace unifies Zoho People, PazCare, ComplyKaro, and M365 behind one AI-searchable front door — turning a 6-app, 15-minute HR errand into a 30-second chat query."

## 3. Why Not Just Extend Microsoft Viva?
Since OneSpace integrates M365/Teams, this comparison will come up — addressed up front:

Viva covers feed, recognition, and wellness within the Microsoft ecosystem, but has no native path into Zoho People, PazCare, or ComplyKaro — the systems that actually hold leave, benefits, and compliance data for many mid-size Indian enterprises. OneSpace's differentiation is being the integration layer across a heterogeneous stack, not a better version of any single tool.

## 4. Scope: Phase 0 → Phase 1 → Phase 2 → Phase 3

Flat feature lists don't survive a build. Scope is split by what proves the core hypothesis first (that unification + AI search drives adoption) before layering engagement features. **Phase 0 is a 4-week, 4-person proof-of-concept** carved out of Phase 1 to validate the riskiest technical assumption — cross-system AI query — before committing to the full Phase 1 build.

| Phase | Features | Goal |
|---|---|---|
| **Phase 0 (POC, 4 weeks)** | AI Assistant only — policy lookup + leave balance, read-only, across Zoho People, ComplyKaro, M365 | De-risk the integration/auth model before wider build; internal demo-ready |
| **Phase 1 (MVP)** | Company Feed, Employee Helpdesk, AI Assistant (policy lookup + leave balance, read-only), Smart Notifications, Idea Hub | Prove the hub reduces time-to-answer and gets daily traffic |
| **Phase 2** | Kudos Wall, Polls, Room Booking, Benefits Management, Timesheet Reminders | Deepen engagement once core habit is formed |
| **Phase 3** | Carpooling, Photo Wall, Employee Communities, mini-games, Automated OOO | Culture/delight layer, lowest urgency |

**Why the split:** the original 4-week/2-dev/2-QA constraint cannot deliver Company Feed + Helpdesk + AI Assistant + Notifications + Idea Hub (full Phase 1) in that window. Phase 0 isolates the AI Assistant — the core differentiator and the highest-uncertainty component (multi-system auth, retrieval design) — as a standalone proof point. Phase 1's remaining features (Feed, Helpdesk, Notifications, Idea Hub) are scoped separately once Phase 0 validates the integration approach.

## 5. AI Assistant — Scope Definition (Phase 0 / Phase 1)
What it can actually do, explicitly — this determines integration complexity by an order of magnitude.

* **V1 (read-only):** Answer policy questions, surface leave balance, locate documents, route helpdesk queries to the right team.
* **V2 (transactional, later):** Apply for leave, raise a ticket, submit a reimbursement — requires write-access to source systems with proper approval workflows.

Starting read-only de-risks the build and avoids owning transactional failures in HR/payroll-adjacent systems on day one.

### 5.1 Phase 0 In Scope
* **Conversational query interface** — single chat UI where an employee can ask natural-language questions ("What's my leave balance?", "Where's the WFH policy?").
* **Read-only integrations** with:
  - Zoho People (leave balance, employee profile data)
  - ComplyKaro (compliance/policy documents)
  - M365/Teams/Outlook (calendar availability, basic profile lookup)
  - *(PazCare integration deferred to Phase 1 pending OAuth capability confirmation — see Section 8, Open Items)*
* **Retrieval approach:** live API calls per query (tool-calling pattern) for real-time data (leave balance); a lightweight indexed/synced store for static content (policy documents), refreshed on a scheduled basis.
* **Authentication:** OIDC SSO for the OneSpace app itself; per-user OAuth token exchange (on-behalf-of) for each downstream integration, so every query and data access maps to the requesting employee's own identity and permissions — not a shared service account.
* **Basic query logging** for support/debugging.

### 5.2 Phase 0 Out of Scope
* Write-back actions (leave requests, reimbursement claims, compliance acknowledgments)
* Company Feed, Employee Helpdesk, Smart Notifications, Idea Hub (full Phase 1 features)
* PazCare integration (pending OAuth confirmation)
* Full compliance-grade audit trail (basic logging only — see Section 6 for what Phase 1 must add)
* Admin CMS / policy management dashboard — ComplyKaro remains the source of truth
* Mobile app — web-based only

## 6. Data Sensitivity & Access Control
OneSpace aggregates HR data (Zoho People), health/benefits data (PazCare), and compliance/legal data (ComplyKaro) behind one interface with an AI layer reading across all of it. This is the first question from any security review — addressed directly:

* **Role-based access control per data source** (not a flattened permission model)
* **Audit logging on every AI-assistant query touching HR/health data** — Phase 0 ships basic query logging (see 5.1); a dedicated, immutable audit layer covering all HR/health/compliance queries is required before Phase 1 handles PazCare (benefits/health) data
* **Data residency and encryption-at-rest/in-transit commitments**
* **Clear boundary:** the AI assistant queries source-system APIs at query time rather than duplicating sensitive data into a central store, where feasible (see FR-3, hybrid retrieval)

## 7. Target Users
* **Employee (primary)** — wants fast answers without navigating multiple portals.
* **HR/Compliance stakeholder (secondary, Phase 0-light)** — cares that data returned respects the employee's own access level; no dedicated admin tooling in Phase 0 beyond what's needed to configure integration credentials.

## 8. Functional Requirements (FR) — Phase 0

### FR-1: Conversational Query Interface
* Single chat input; responses rendered as natural language, with source system attribution ("From Zoho People", "From ComplyKaro policy doc") so the employee knows where the answer came from.
* Must handle at minimum: leave balance, policy document lookup, calendar/availability lookup.

### FR-2: Per-User Authenticated Integration Access
* OIDC login on the OneSpace client; access token attached to backend requests.
* Backend performs per-integration token exchange so each downstream call (Zoho People, ComplyKaro, M365) executes as the requesting employee, not a shared credential.
* No employee can retrieve another employee's data through OneSpace.

### FR-3: Hybrid Retrieval
* Real-time queries (leave balance) → live API call to source system at query time.
* Static/document queries (policy lookup) → indexed store synced from ComplyKaro on a scheduled interval (e.g., daily), to keep search responsive.

### FR-4: Query Logging
* Log query text, resolved intent, systems called, and response summary per user/session — for support and POC-stage debugging. Not a compliance audit trail (see Section 6).

### FR-5: Zoho MCP Tool Allowlist Enforcement
* The Zoho People resolver must maintain a hard-coded allowlist of callable MCP tool names, enforced in code before any `tools/call` request is issued — never inferred from prompt instructions alone.
* **Phase 0 allowlist (active scope):** `getLeaveBalance`, `fetchLeaveTypes`, `listholidays`, `getLeaveRecords` (self-scope only), `getTeamMembersOnLeave`, `getEmployeeShiftDetails`.
* **Full read-only candidate set** (53 tools, for Phase 1+ expansion) is catalogued in Appendix A / `zoho_people_readonly_tools.json`. Two tools in that broader set are flagged as admin-scoped despite being read-only (`fetchLeaveTypeDetails`, `getAdminEnrollmentBeneficiaries`) and should stay excluded from an employee-facing assistant even when the allowlist is widened.
* Any tool call outside the active allowlist (including all write/admin actions listed in Section 14, and the two admin-flagged read tools above) must be rejected by the resolver before it reaches the MCP server, and logged as a blocked attempt (feeds FR-4 logging).
* This allowlist is reviewed and extended only as a deliberate scope decision, not expanded implicitly when new tools appear in `tools/list`.

## 9. Non-Functional Requirements (NFR)
* **Response time:** target under 3 seconds for real-time queries (excluding LLM generation latency), under 1 second for indexed document search.
* **Security:** TLS in transit; no credentials or tokens persisted beyond session; per-user OAuth scoping enforced on every downstream call (see FR-2); Zoho MCP tool calls restricted to the FR-5 allowlist regardless of what the LLM requests.
* **Availability:** best-effort for Phase 0 — no HA/failover requirement at this stage given the 4-week timeline.

## 10. Success Metrics (Phase 1 onward)
Measurable targets, not "improves engagement/productivity/compliance":

* **Helpdesk deflection:** % of HR/IT queries resolved by AI assistant without a ticket
* **Time-to-answer:** median time from question asked → answer received (target: minutes, not hours)
* **Adoption:** DAU/MAU on the hub within 90 days of rollout
* **Engagement programs:** % increase in participation on Idea Hub / Kudos Wall / Polls vs. pre-OneSpace baseline

## 11. Rollout Strategy (Phase 1 onward)
Single-hub platforms typically fail on adoption, not technology:

* Pilot with one department/team (4–6 weeks) → gather feedback → org-wide rollout
* Decide early: opt-in vs. mandatory (mandatory + strong onboarding tends to outperform opt-in for utility tools like this)
* Executive sponsorship visible in the Company Feed at launch to signal top-down backing

## 12. Open Items (Needs Decision Before Phase 0 Build)
1. **Proof point for the "15-min → 30-sec" claim** — needs a rough time-study or estimate breakdown to defend in review/demo.
2. **Confirm OAuth on-behalf-of support** across integrations — Zoho People and M365 support this natively; PazCare and ComplyKaro's OAuth/API capabilities need verification. PazCare is deferred out of Phase 0 pending this; ComplyKaro's document-API access also needs confirming.
3. **Sync interval for ComplyKaro documents** — daily vs. on-demand, depending on how often policies change.
4. **Audit layer design** — must be resolved before Phase 1 introduces PazCare (health/benefits) data, per Section 6.
5. **Zoho MCP server auth model** — confirm how `https://<your-org>.zohomcp.in/mcp/` authenticates inbound requests (per-user token pass-through vs. a server-level credential) before relying on it for FR-2's "no employee can retrieve another employee's data" guarantee. This is a test endpoint (`test-...`), so the production endpoint and its auth model also need confirming before Phase 1.

## 13. Milestones — Phase 0 (4 Weeks)
* **Week 1:** OIDC auth + chat UI shell; confirm integration API/auth capabilities (resolves Open Item 2). See Section 13.1 for daily breakdown.
* **Week 2:** Zoho People + M365 integration (live query path); basic query logging.
* **Week 3:** ComplyKaro integration; document indexing/sync pipeline.
* **Week 4:** End-to-end testing, query intent coverage hardening, demo prep.

### 13.1 Week 1 Detail — Foundation & Integration Feasibility

**Goal:** OIDC login works end-to-end, chat UI shell renders, and it's confirmed which integrations support per-user OAuth — this determines whether the FR-2 auth model holds as designed or needs a fallback.

| Day | Dev 1 | Dev 2 | QA (both) |
|---|---|---|---|
| 1 | Set up Angular shell + routing, OIDC client config | Set up .NET backend project, OIDC token validation middleware | Review PRD, draft test plan for auth flow |
| 2 | Wire Angular → backend token attachment (Bearer header) | Backend: claims extraction (sub/nameidentifier), user context resolution | Environment/tooling setup, start integration API research (Zoho People + M365 docs) |
| 3 | Chat UI shell: message list, input, loading states (no live data yet) | Zoho People API: confirm OAuth on-behalf-of support, test token exchange | PazCare API research: does it expose per-user OAuth or only API-key/service-account access? (informs Phase 1 scoping) |
| 4 | Wire chat UI to a stub backend endpoint (echo response) | M365 Graph API: confirm on-behalf-of flow for calendar/profile scopes | ComplyKaro API research: same OAuth question; also check if it exposes document content via API at all (vs. manual export) |
| 5 | Buffer / fix auth issues found by QA | Buffer / fix integration findings | **Decision checkpoint:** consolidate findings — which systems support per-user OAuth, which need a fallback (service account + scoped query, or manual data sync) |

**Exit criteria for Week 1:**
- [ ] Employee can log in via OIDC and reach the chat shell
- [ ] Backend validates tokens and resolves the correct user identity
- [ ] Confirmed, in writing, per integration: OAuth on-behalf-of supported / not supported
- [ ] For any integration where OBO isn't supported, a fallback auth approach is chosen before Week 2 starts

## 14. System Architecture (Phase 0)

**Stack:** Angular (client) · .NET backend with a GraphQL layer · MongoDB (context/session store)

```
        [ Angular Client ]
               │  (OIDC token, HTTPS)
               ▼
   [ .NET Backend — GraphQL API ]
       │            │            │
       ▼            ▼            ▼
 [Zoho People]  [ComplyKaro] [ M365 Graph ]
 via Zoho MCP     (resolver)    (resolver)
 (JSON-RPC)
       │
       ▼
 https://<your-org>.zohomcp.in/mcp/
               │
               ▼
        [ MongoDB ]
   - Query/session logs (FR-4)
   - Indexed ComplyKaro doc cache (FR-3)
```

* **Angular client** — chat UI (FR-1), OIDC login, attaches Bearer token to all GraphQL requests.
* **.NET GraphQL API** — single endpoint fronting all three integrations. Each data source is exposed as its own resolver/field, so the AI assistant's intent-routing layer resolves a query ("leave balance" → Zoho People resolver; "WFH policy" → ComplyKaro resolver) into a single GraphQL query rather than separate REST calls per system. This maps cleanly onto FR-3's hybrid retrieval: real-time resolvers (Zoho People, M365) hit the source system per request; the ComplyKaro resolver reads from the MongoDB-indexed doc cache.
* **Zoho People resolver → Zoho MCP server** — rather than calling Zoho People's REST API directly, the Zoho People resolver connects to the Zoho MCP endpoint (`https://<your-org>.zohomcp.in/mcp/`) over JSON-RPC (`tools/list` to discover available capabilities, `tools/call` to execute a query — e.g. fetch leave balance). This is the pattern already established in the separate Zoho MCP Corporate AI Assistant PRD; OneSpace's GraphQL resolver becomes a thin client over that same MCP surface rather than a second, independent Zoho integration.
* **⚠️ Tool allowlist is mandatory, not optional.** The server at `server_id=<server-id>` exposes ~95 tools covering the full Zoho People surface — including write and admin actions (`applyLeave`, `cancelLeave`, `deleteLeaveType`, `editLeaveType`, `terminateEnrollment`, `approveEnrollment`/`denyEnrollment`, `addTimelog`/`editTimelog`/`deleteTimelogs`, `unpublishBenefitPlan`, `addAdminEnrollment`, and others) alongside read-only ones. Phase 0 is scoped as read-only end-to-end, so the resolver must enforce a hard allowlist of callable tool names at the code level — not rely on prompt instructions telling the LLM to "only read data." An LLM misinterpreting a query, or a prompt-injection scenario via tool-result content (flagged in the earlier Zoho MCP Corporate AI Assistant PRD review), must not be able to reach a write tool because it was never in the resolver's callable set, regardless of what the model requests.
  - **Phase 0 allowlist (read-only):** `getLeaveBalance`, `fetchLeaveTypes`, `listholidays`, `getLeaveRecords` (self-scope only — `dataSelect=MINE`), `getTeamMembersOnLeave`, `getEmployeeShiftDetails`.
  - **Explicitly excluded from Phase 0:** all `apply*`, `cancel*`, `add*`, `edit*`, `delete*`, `approve*`, `deny*`, `terminate*`, `waive*`, `withdraw*`, `publish*`/`unpublish*`, `enable*`/`disable*` tools, and any `getLeaveRecords`/`getEmployeeShiftDetails` call with `dataSelect` broader than the requesting employee's own records (department/org-wide scopes are an admin capability, not part of Phase 0's employee-facing assistant).
* **Per-user auth (FR-2)** — for M365 and (if added) direct-REST integrations, the GraphQL layer performs per-integration on-behalf-of token exchange. For the Zoho People *MCP* path specifically, auth is whatever the MCP server itself enforces on inbound requests (see Open Items — needs confirming, since MCP's auth model may differ from a standard per-user OAuth exchange).
* **MongoDB** — two collections for Phase 0: query/session logs (FR-4) and the synced ComplyKaro document index (FR-3). Not used to store live HR/profile data — that stays source-of-truth in Zoho People (via MCP) and M365, fetched at query time, per the data-boundary principle in Section 6.

**Why GraphQL here specifically:** the AI assistant's core job is aggregating fields from multiple heterogeneous systems into one answer — GraphQL's field-level resolution avoids the Angular client (or backend orchestration code) having to stitch together multiple REST responses per query, and gives a single typed schema to extend as Phase 1 adds PazCare and more query types.

### 14.1 Recommended Tech Stack

| Layer | Choice | Why |
|---|---|---|
| **Frontend** | Angular (latest LTS), standalone components | Matches existing stack; chat UI + OIDC login are straightforward SPA concerns |
| **API layer** | .NET 8/9 + HotChocolate (GraphQL for .NET) | Mature, schema-first, fits the resolver-per-integration pattern above |
| **AI orchestration** | Anthropic/OpenAI API via a thin intent-routing service inside the .NET backend | Keeps LLM calls in the same backend that already owns auth/resolver logic — avoids a second orchestration layer to secure |
| **Retrieval (policy docs)** | MongoDB Atlas Search (vector + text) on the ComplyKaro doc cache | Already on MongoDB — avoids standing up a separate vector DB (Pinecone/Weaviate) for Phase 0 scale. Revisit if document volume/query sophistication outgrows it in Phase 1/2 |
| **Data store** | MongoDB (sessions, query logs, doc index) | Per architecture above — no live HR data stored here |
| **Auth** | OIDC provider (Azure AD / Entra ID, given M365 dependency) + per-integration OAuth token exchange | Entra ID as IdP simplifies the M365 on-behalf-of flow specifically, since it's the same tenant |
| **Integration clients** | Zoho People via Zoho MCP server (`<your-org>.zohomcp.in`, JSON-RPC), Microsoft Graph SDK, ComplyKaro REST/API (TBD per Open Item 2) | Zoho People goes through the existing Zoho MCP surface rather than a separate REST integration; M365/ComplyKaro remain typed HTTP clients per resolver |
| **Hosting** | Azure App Service (API) + Azure Static Web Apps or App Service (Angular) | Azure-native, consistent with existing SDLC pipeline direction |
| **CI/CD** | Azure DevOps or GitHub Actions | Whichever the org already standardizes on |
| **Observability** | Application Insights | Query logging (FR-4) can piggyback on this rather than a separate logging stack |

## Appendix A — Zoho People MCP: Read-Only Tool Inventory

Filtered from the full 95-tool inventory on server_id `<server-id>` (`<zoho-mcp-console-url>`). Write/admin/mutating tools removed — see FR-5 and Section 14 for enforcement. **Bold** entries are the active Phase 0 allowlist; the rest are candidates for Phase 1+ expansion.

**Active Phase 0 (6):** **getLeaveBalance** · **fetchLeaveTypes** · **listholidays** · **getLeaveRecords** (self-scope only) · **getTeamMembersOnLeave** · **getEmployeeShiftDetails**

**Phase 1+ candidates (47, excludes 2 admin-flagged read tools noted below):**
getPlansForSpecificEnrollmentWindow · getDepartmentShiftGrouping · fetchLeaveBasicInfo (internal-use only, per Zoho's own description — not for direct user exposure) · getUserEnrollmentDetail · getLeastProjectUsers · getTopProjectJobs · getApplicableShiftsForUser · getEligiblePlans · getBenefitCategoryDetail · getShiftChangeApprovals · getUserPlanDetail · getProviders · getBenefitPlanDetail · getProjectUsers · getBenefitCategories · getEnrollmentDetail · getUserPlanCoverages · getRunningTimer · getJobSchedule · getUserPlanYears · getProjects · getPlansForPlanYear · getJobs · getEnrollments · getPlanYear · getUserEnrollmentWindows · getPlansForSpecificPlanYear · getProviderDetail · getCompoffRecords · getPlanYearsForPlan · getClients · getEnrollmentStats · getEmployeeBenefitSummary · getPlanYearDetail · getDependentDetail · getTopProjectUsers · getPlansForEnrollmentWindow · getBenefitPlans · getEligibleUsersForPlan · getEnrollmentWindowDetail · getTimelogs · downloadBenefitFile · getUserEnrollmentBeneficiaries · getUserEnrollmentWindowPlans · getEnrollmentWindows · getDependents

**Excluded even though read-only (admin-scoped per Zoho's own tool descriptions):** `fetchLeaveTypeDetails` (marked "Admin-only access"), `getAdminEnrollmentBeneficiaries` (admin view)

**Removed entirely — write/admin/mutating (40):**
addEnrollment · unpublishBenefitPlan · addDependent · addTimelog · editTimelog · cancelcompoff · enableEnrollmentWindow · addAdminEnrollment · addAdminBeneficiary · createBenefitPlan · deleteTimelogs · addLeaveType · deleteLeaveType · publishJobSchedule · disableBenefitPlan · createEnrollmentWindow · createBenefitCategory · editLeaveType · terminateEnrollment · createPlanYear · addProvider · approveEnrollment · completeEnrollmentWindow · enablePlanYear · disableEnrollmentWindow · disablePlanYear · enableBenefitPlan · editJobSchedule · addJobSchedule · addClient · cancelLeave · waiveEnrollment · denyEnrollment · addShiftChangeRequest · withdrawEnrollment · applyLeave · publishBenefitPlan · sumbitTimesheet · startTimer · addWindowEnrollment · addUserBeneficiary
