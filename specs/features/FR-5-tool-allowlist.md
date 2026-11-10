# FR-5 Zoho MCP tool allowlist enforcement

**Status:** Implemented · **Contract:** `specs/mcp/tool-allowlist.json`

## Rules
- Deny by default. A tool is callable only if named **exactly** (case-sensitive) in `allowed`.
- Phase 0 scope: `getLeaveBalance`, `fetchLeaveTypes`, `listholidays`, `getLeaveRecords` (self), `getTeamMembersOnLeave`, `getEmployeeShiftDetails` (self).
- `requiredArguments` are forced (e.g. `data_select=MINE`); a supplied broader value is denied, not rewritten.
- `fetchLeaveTypeDetails` and `getAdminEnrollmentBeneficiaries` are always denied (admin-scoped).
- The spec fails startup if it contradicts itself (write verb or admin tool on the allowlist, or `defaultPolicy` ≠ `deny`).
- `tools/list` results are filtered to the allowlist; new server tools are never exposed implicitly.

## Enforcement points (defence in depth)
1. `AssistantToolbox` checks before any network call (blocked attempts are logged, FR-4).
2. `ZohoMcpClient.CallToolAsync` checks again before `tools/call`. Nothing reaches the MCP server without passing.
3. The model only ever sees allowlisted tools, but the guard never relies on that or on prompt wording.

## Tests
- `OneSpace.UnitTests/Guard/ToolAllowlistGuardTests` — allow/deny, case, scope arguments, invalid specs.
- `OneSpace.UnitTests/Connectors/ZohoMcpClientTests` — a blocked tool makes no HTTP request.
- `OneSpace.ContractTests/ToolAllowlistContractTests` — embedded spec == file; scope == PRD Phase 0; all 41 Appendix A write/admin tools denied.

## Changing scope
Edit the JSON in a reviewed PR → update the contract test's Phase 0 list → CI proves the rest.
