# FR-1 Conversational query interface

**Status:** Implemented (Phase 0) · **Contract:** `ask(input: AskInput!): AssistantAnswer!` in `specs/graphql/schema.graphql`

## Behaviour
- One chat input. Questions up to 2000 characters; blank input is rejected with `BAD_USER_INPUT`.
- Each answer carries `sources[]` (`system` + `label`) so the employee sees where it came from ("From Zoho People", "ComplyKaro policy: …").
- A `sessionId` groups a conversation; the UI resets it on "New conversation".
- Minimum intents: leave balance, policy lookup, calendar/availability.

## Acceptance
- [x] "What's my leave balance?" → Zoho People `getLeaveBalance`, attributed to Zoho People.
- [x] "Where's the WFH policy?" → policy search, attributed to the ComplyKaro document title.
- [x] "Am I free this week?" → Outlook calendar (Graph, OBO), attributed to Outlook.
- [x] Requests for actions (apply leave, raise ticket) are declined; the UI notes that actions were not performed.
- [ ] Median real-time answer < 3 s excluding LLM generation (NFR) — measure in Week 4 against real integrations.

## Where
API: `OneSpace.Api/GraphQL/Mutation.cs`, `OneSpace.Application/Assistant/AskAssistantUseCase.cs`, `OneSpace.Assistant/Engines/*`.
UI: `ui/src/app/features/assistant/`.
