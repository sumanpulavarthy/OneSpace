# ADR 0004: Micro-frontend UI with Native Federation

**Status:** Proposed (2026-11-19), for review by Rahul (Enterprise Architect)

**Context:** The production backlog has 25 epics (Feed, Ideas, Kudos, Helpdesk, Room Booking, HR Policies, Academy, Chat, Games …). We want small teams to build and release their own UI modules without waiting on, or breaking, other teams. Today the UI is a single Angular 20 app (`ui/`), so every change ships in one build and one release.

**Decision:** Split the UI into a **shell (host)** and independently deployed **remotes**, one per domain team, using **Native Federation** (`@angular-architects/native-federation`). This works with Angular's esbuild application builder and uses browser-native ES modules and import maps.

- **Shell** owns: sign-in (MSAL), layout and navigation, the home dashboard, the design system, the GraphQL client, notifications, error handling and telemetry.
- **Remotes** expose an Angular `Routes` array and get a top-level route in the shell (`/feed`, `/helpdesk`, `/policies` …).
- **Runtime manifest:** the shell loads `federation.manifest.json` from configuration at start-up. A new or updated remote goes live by changing the manifest, without rebuilding the shell. Entries can be limited to an audience (Entra group) or switched off with a feature flag.
- **Shared singletons:** Angular, RxJS, MSAL, the Apollo client and `@onespace/ui-kit` are shared at strict versions, so only one copy of each loads.
- **Contract:** remotes can't call each other directly. They use shell services such as `AuthContext`, `UserContext` and `Navigation`, and talk through an event bus. Each remote's data comes from its own module in the one GraphQL schema (spec-driven, as today).
- **Repository:** one Nx monorepo (`ui/apps/shell`, `ui/apps/<remote>`, `ui/libs/*`). Each app has its own pipeline triggered by path filters. Ownership is set per folder with CODEOWNERS.
- **Hosting:** each remote has its own versioned path in Storage/CDN (`/remotes/<name>/<version>/`) behind Front Door. The shell stays on its Static Web App.

**Alternatives considered:**
- *Monolithic Angular app with lazy-loaded feature libraries (Nx):* simplest, but it keeps one release train, which is what we are trying to remove.
- *Webpack Module Federation:* Angular no longer uses webpack by default; Native Federation is the esbuild equivalent.
- *Web Components (Angular Elements):* lets teams use different frameworks, but the bundles are larger and routing and DI integration are weaker. Keep this only for a team that must use another framework.
- *iframes:* strong isolation, but the UX is poor (scrolling, sizing, deep links, auth).

**Consequences:**
- Teams build, test and deploy their own remotes on their own schedule.
- Shared dependency upgrades (Angular major versions) must be coordinated across all remotes. Plan this as a quarterly platform task.
- More moving parts: version skew between shell and remotes, CORS/CSP for remote origins, and checks that every remote's `remoteEntry.json` is reachable after deploy.
- UX stays consistent only if every team uses `@onespace/ui-kit` and the shell layout. A lint rule blocks direct imports between remotes.
- The backend stays a modular monolith (ADR 0001). If teams later need independent API releases, HotChocolate Fusion can compose per-team GraphQL subgraphs without changing what the UI calls.
