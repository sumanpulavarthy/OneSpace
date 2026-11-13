# FR-2 Per-user authenticated integration access

**Status:** Implemented. M365 uses on-behalf-of; Zoho uses per-employee MCP OAuth with encrypted refresh tokens (decided 2026-11-10).

## Rules
1. The UI signs in with Entra ID (OIDC, MSAL redirect) and sends `Authorization: Bearer` on every GraphQL call.
2. The API validates the token (issuer, audience, signature, lifetime). Outside Development, missing `AzureAd` config fails startup.
3. Every resolver acts as the caller. The schema has **no argument that selects another employee** (contract test enforces this).
4. Microsoft 365: on-behalf-of exchange per request (`OnBehalfOfTokenProvider`); Graph applies the employee's own permissions.
   - Read-only and `/me`-scoped only: profile, calendar, manager and direct reports, presence, mail (subject/sender/preview, never bodies), OneDrive/SharePoint files, joined Teams, chats (last-message preview), open To Do tasks.
   - Delegated permissions (grant with admin consent on the API app registration): `User.Read`, `Calendars.Read`, `Mail.Read`, `Files.Read.All`, `Team.ReadBasic.All`, `Chat.Read`, `Tasks.Read`, `Presence.Read`. Configurable via `Integrations:Microsoft365:Scopes`.
5. Zoho People (MCP): each employee connects once ("Connect Zoho People"). Every MCP request carries **their** Zoho OAuth token, so Zoho enforces their permissions. Defence in depth, using argument names verified against the live schemas on 2026-11-10:
   - The guard forces `data_select=MINE` on `getLeaveRecords`.
   - The MCP client removes every argument that selects other people (`employee`, `erecno`, `employee_zoho_ids`, department and location filters, …), so Zoho falls back to the token's own employee. These arguments are also hidden from the model.
   - `getEmployeeShiftDetails.emailId` is bound to the caller.
6. Token storage (**NFR amended**, security sign-off required):
   - Entra tokens are never persisted: MSAL in-memory cache server-side, sessionStorage in the browser.
   - Zoho **refresh tokens are persisted encrypted** (ASP.NET Core Data Protection; the key ring sits in Cosmos, wrapped by a Key Vault RSA key). Access tokens are encrypted the same way. PKCE verifiers are encrypted. The OAuth `state` is stored only as a SHA-256 hash, is single use and expires after 10 minutes.
   - Disconnect revokes the token at Zoho (RFC 7009) and deletes it. A refresh rejected with `invalid_grant` deletes the stored token and prompts the employee to reconnect.
   - If the key ring is lost, stored tokens are discarded (employees reconnect). They are never left half-readable.

## Zoho MCP auth: resolved 2026-11-10 (Open Item 5)
- The server URL has the form `https://<your-org>.zohomcp.in/mcp/<server-key>/message`. The URL contains a secret: keep it in Key Vault (`Integrations--ZohoMcp--Endpoint`) or user-secrets locally, never in appsettings.
- Called with only the key in the URL, the server returns `401 Authentication required` with `WWW-Authenticate: Bearer resource_metadata=…/.well-known/oauth-protected-resource`. The key identifies the server; it is **not** a credential.
- The server implements **MCP OAuth** (RFC 9728 + RFC 8414):
  - authorization-code grant with PKCE (S256), plus refresh tokens;
  - dynamic client registration;
  - client auth `none` or `client_secret_post`;
  - authorize and token endpoints on `mcp.zoho.in/.../<server-id>/...`.
- **Consequence: Zoho access is per employee.** Each employee signs in to Zoho once and grants consent. Zoho then enforces their own permissions, which is exactly FR-2. Entra OBO can't produce these tokens.
- Scopes offered: `zohopeople.leave.all`, `.attendance.all`, `.timetracker.all`, `.benefits.all`, `.forms.all`, `ZohoMCP.tool.execute`. These are `.all` scopes (read **and** write), and no read-only scope exists. The FR-5 allowlist guard is therefore the only thing keeping Phase 0 read-only (ADR 0003).
- Phase 0 needs `zohopeople.leave.all`, `zohopeople.attendance.all` (shifts) and `ZohoMCP.tool.execute`.

## "Connect Zoho People" (implemented)
1. A Zoho tool needs an employee who hasn't connected. The tool call ends with outcome `CONNECTION_REQUIRED`, and the answer shows a **Connect Zoho People** button.
2. `connectZoho` discovers the auth server (RFC 9728/8414). On first use it registers OneSpace (RFC 7591); the client secret is stored encrypted in `oauthClients`. It then creates a PKCE (S256) authorization bound to the employee and returns Zoho's consent URL.
3. Zoho redirects to `GET /oauth/zoho/callback`. That endpoint is anonymous, and the state authenticates it. The API exchanges the code (with the verifier and the `resource` parameter) and stores the encrypted tokens under the employee's Entra `oid`. It then redirects to the fixed UI URL with `?zoho=connected|failed`.
4. `ZohoMcpClient` sends `Authorization: Bearer <employee's token>`. The token is refreshed 60 s before expiry, or once on a 401, and rotated refresh tokens are kept. Refreshes are serialised per employee.
5. `disconnectZoho` revokes the token and deletes it.

Code: `OneSpace.Connectors/Zoho/ZohoOAuthService.cs`, `OneSpace.Infrastructure/Persistence/OAuthStores.cs`, `OneSpace.Infrastructure/Security/DataProtectionSetup.cs`.
Tests: `UnitTests/Connectors/ZohoOAuthServiceTests` (full flow against a fake auth server) and `IntegrationTests/OAuthStoreTests` (ciphertext at rest, single-use hashed state).

## Live findings (2026-11-10, after a real employee connected)
- Tool names on the wire are prefixed: `ZohoPeople_getLeaveBalance`. The client maps them (`ToolNamePrefix`); the guard and spec keep the PRD's bare names.
- Arguments are wrapped as `{ "body": { "arguments": { … } } }`.
- Dates use `dd-MMM-yyyy`. `listholidays` needs `from`/`to`, and `getLeaveRecords` needs `from_date`/`to_date`.
- **Blocker (Zoho admin):** calls currently return `MCP_DISABLED: MCP is disabled for your organization… enable Zia in Zoho People Settings`. A Zoho People admin must enable Zia/MCP.
- **Verified 2026-11-12:** Zoho rejects `getLeaveBalance` without `employee` (erecno) and resolves `CURRENTUSER` to empty for OAuth clients; no allowlisted tool returns the caller's erecno. OneSpace therefore binds the caller's own erecno from an admin-maintained directory (`zohoEmployees` collection, keyed by work email; `ZohoMcp:EmployeeErecnos` config as dev fallback) via `ZohoMcp:SelfErecnoArguments`. A model-supplied value is always overwritten; unmapped employees get a "not linked yet" answer.
- **Auto-resolved 2026-11-13:** when no mapping exists, OneSpace finds the erecno through the employee's own Zoho connection: `GET people.zoho.<dc>/people/api/attendance/getUserReport` (today only) with their MCP token, taking `employeeDetails.erecno` only from the entry whose `mail id` matches their verified Entra email (an admin token lists everyone, so there is no fallback to another entry). The result is cached in `zohoEmployees`. Dev: set `Authentication:DevelopmentUserEmail` to a real Zoho employee email.

## Open
- **Open Item 2:** ComplyKaro and PazCare OAuth capability.
