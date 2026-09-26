# Cardflow engineering experience

Last updated: 2026-09-27, after the passing hosted Phase 4 run.

This is a living account of how we built Cardflow, what we actually ran, and how we diagnosed mistakes. It complements [`architecture.md`](architecture.md), which describes the intended system, and [`codex-prompt.md`](codex-prompt.md), which defines the phase gates. Add to this file whenever a later phase changes the design, exposes a failure, or gains new verification. Do not rewrite a failed attempt as if it never happened.

## Working agreement and current position

- Build one phase at a time. Do not start the next phase until the user asks.
- Explain each small implementation in chat so the user can learn the reasoning.
- Run the app and tests before claiming success. A local pass is not a GitHub Actions pass.
- Commit logical units. Red tests and real mistakes may appear in history, but leave the phase in a passing state.
- Ask the user for manual work such as `git push` and cloud setup. The user puts long logs in the ignored `temp-ref/` directory.
- Phases 0–4 have passing GitHub Actions runs. Phase 4 (presence) was verified locally and on the pushed commit `5ca6544`. Phase 5 has not started.

The only untracked file currently visible outside this document is `src/Cardflow.Api/Properties/launchSettings.json`; it belongs to the local environment and has deliberately not been staged.

## Where the pieces are

| Concern | Main location | Why it is there |
|---|---|---|
| Browser app | `web/src/App.tsx`, `web/src/boardState.ts` | React view and pure event/rollback rules |
| HTTP endpoints | `src/Cardflow.Api/Program.cs` | ASP.NET Core Minimal APIs; there is no controller class |
| Board rules | `src/Cardflow.Api/Boards/BoardService.cs` and `BoardCommandService.cs` | Both REST and SignalR use the same business logic |
| Real-time entry | `src/Cardflow.Api/Realtime/BoardHub.cs` | Thin hub: authorize/join, call a service, broadcast |
| Persistence | `src/Cardflow.Api/Data/` | EF Core model and PostgreSQL migrations |
| Local stack | `compose.yaml`, `infra/nginx/default.conf` | PostgreSQL, Redis, two APIs, nginx |
| CI | `.github/workflows/ci.yml` | Build, database, two-instance, convergence, and browser checks |

The browser is intentionally separate from Docker Compose at this stage. Compose runs the backing services; Vite serves the browser during development. From the repo root in PowerShell:

```powershell
docker compose up -d --build --wait
cd web
npm.cmd ci
npm.cmd run dev
```

Open `http://127.0.0.1:5173`. `npm.cmd` is used because PowerShell on this machine blocks the `npm.ps1` shim. Static browser hosting is not yet part of the Compose stack.

## How we approached each phase

### Phase 0 — Make the environment real first

We began with the requested repository layout, a .NET 10 API solution, `/health`, Docker Compose with PostgreSQL and Redis, **two** API instances behind nginx, and a GitHub Actions workflow. The first test used Testcontainers to connect to a real PostgreSQL server. The user confirmed Docker 29.6.1 was running. Two instances mattered from day one because a real-time bug can remain invisible with only one process. We committed this foundation as `d968282`.

The user could not paste long CLI output into chat, so we ignored `temp-ref/` in Git (`eb3202a`). That directory is a diagnostic hand-off, not project source. We read logs from it and do not stage it.

### Phase 1 — Board state before real-time delivery

We added boards, members, columns, cards, anonymous session cookies, join-by-secret-link, and REST operations (`717ce8a`). PostgreSQL migrations create the real schema. A snapshot returns columns, cards, members, and later the board's event sequence.

The difficult part was card order. Integer positions require updating many rows when inserting in the middle. We used canonical binary fractional ranks instead: a normal insert computes a rank between neighbours and writes only its own row. Equal ranks sort by card ID, so concurrent same-gap inserts still have one deterministic order. A minute-by-minute background sweep redistributes ranks if they grow beyond 50 characters. Tests covered start/middle/end ranks, repeated insertion, one-row writes, stable tie-breaking, membership, and stale-version rejection.

Two design corrections are recorded in the architecture decision log: arbitrary strings do **not** always have a midpoint (prefix-adjacent values break that claim), so generated ranks use a canonical binary form and PostgreSQL `C` collation (D9). The earlier wording “last write wins” contradicted rejecting a stale version; the intended rule is that the first valid edit wins and the later stale edit gets a conflict (D10). We changed the explanation rather than hiding the contradiction.

### Phase 2 — Event transaction, then cross-instance delivery

We first committed a failing schema test (`994fd60`): it expected `board_events` and `boards.event_seq`, but the old schema had neither. This made the missing behavior visible. The migration (`eeac849`) added a per-board sequence and an append-only event table.

Next, `BoardCommandService` wrapped each production mutation and its event insert in **one PostgreSQL transaction** (`4fd0361`). An `UPDATE boards ... RETURNING event_seq` assigns a monotonic sequence for that board. A test forced the event insert to fail and verified that the card row and sequence increment rolled back too. This is stronger evidence than merely checking that both calls exist in code.

The SignalR hub stayed thin: it checks membership through services, joins a board group, calls a command service, and broadcasts the committed event. Redis is the SignalR backplane, so a socket on API 1 can hear a write handled by API 2 (`3b5e9c3`). A separate two-instance test and CI step prove both REST-to-socket and hub-to-socket delivery (`f23fa91`). We also made broadcasting independent of the caller's cancellation after commit: if a caller disconnects after the database commit, the accepted write remains accepted, and clients can later catch up from the event log.

The architecture initially combined fallback SignalR transports with a claim that sticky sessions were unnecessary. Those claims do not hold together for this setup. We chose WebSockets-only with negotiation skipped and documented the decision (D11). If fallback transports are added later, session affinity must be revisited.

### Phase 3 — Rollback first, then reconnect and convergence

We started with a red catch-up test (`19ab67c`), then implemented `CatchUp` (`232d3be`). The client supplies the last sequence it applied. The server replays up to 500 contiguous events; if it is more than 500 behind or any event in that interval is missing, it returns a fresh snapshot. The client joins the SignalR group before catch-up and buffers any live events that arrive during the catch-up call. This closes the gap between the original REST snapshot and the socket opening.

Events are a bounded reconnect buffer, not a permanent audit log. A periodic prune removes events older than seven days or outside the newest 5,000 per board. A database test proved both deletion rules and snapshot fallback over a pruned gap (`14cb333`).

The React/Vite board (`a2fab83`) uses TanStack Query for the first REST snapshot, SignalR for live changes, and dnd-kit for dragging. Confirmed server state is kept separate from at most one pending move. The pending move changes the visible order immediately; a failed hub invocation discards it, restoring the untouched confirmed state. Unit tests cover cross-column and within-column rollback, duplicate events, and gap detection. The browser sends neighbour card IDs; the server computes the canonical rank, so rank logic is not duplicated in TypeScript (D12). One board model was small enough for React state plus a pure projector, so we did not add Zustand (D13).

The phase's headline test starts 20 simulated SignalR clients across the two API instances, fires 200 concurrent mixed card operations, waits for all clients to reach the final sequence, and compares each card order with a fresh PostgreSQL snapshot. Playwright also proves an accepted online drag, rollback after the WebSocket is severed, and replay of 50 changes made while a browser is disconnected. These tests were added to CI (`26888fc`).

## Debugging journal: evidence → cause → change → proof

### A test can be wrong before the feature is wrong

The first Phase 3 test did not compile because a raw interpolated C# SQL string contained JSON `{}`. We replaced that literal with `jsonb_build_object()`, then got the **intended** red failure: `BoardSyncService` did not exist. Later, the 600-event fallback test failed even after implementing catch-up. The test had updated PostgreSQL with raw SQL but reused an EF context tracking the old board sequence. A reconnect is a new request with a fresh context; changing the test to use a fresh context made it model production correctly. Lesson: when a test fails, inspect the fixture and its cached state before changing application logic.

### A compile error caught an incorrect assumption

While building the two-instance test, we treated `CookieContainer` as `IDisposable`. It is not. The compiler stopped the build; we removed that disposal and rebuilt. We explained this as a real mistake rather than fabricating an error for the commit history.

### A stress test found database connection exhaustion

The first 20-client/200-operation run produced PostgreSQL SQLSTATE `53300` (“too many clients already”). Both API instances could open enough pooled connections to exceed PostgreSQL's default connection limit. Many HTTP errors then appeared as JSON parse errors because the development server returned plain-text exception pages. We capped each API pool at 20 connections in Compose, so excess requests wait for a pooled connection. We also made the test report only a few concise failures. The unchanged 200-operation workload then passed. Lesson: the first *server* error in a noisy failure report matters more than dozens of follow-on client parse errors.

### Browser tests distinguished a visual guess from a commit

The first Playwright drag dropped over the source rather than the empty destination. We excluded the active card from dnd-kit collision candidates and used an explicit destination point in the browser test. Then the test appeared to fail waiting for `Event #6`: the move had actually committed as `Event #5` (board creation, two columns, card creation, then move). We traced the drop IDs, hub completion, live event, and catch-up sequence before correcting the assertion. The final test waits for the committed sequence before severing the WebSocket, then verifies the next drag rolls back. Lesson: seeing a card move optimistically is **not** evidence that the server accepted it.

### CI failure 1: nginx was sampled during startup

An early GitHub Actions log stopped in “Check nginx and both API instances.” The check made 12 rapid `/health` requests and saw only `api1`. Container logs showed nginx had tried both API processes before they were listening, briefly marked the upstreams unavailable, and then recovered. The log's later container shutdown was the workflow's `if: always()` cleanup, **not** the cause. We shortened nginx's upstream failure window and changed CI to wait for both instances rather than deciding from an immediate burst (`81941f4`).

### CI failure 2: the direct check failed before the nginx loop

The next log again stopped in the smoke step, but this time there was no sampled output at all. Its first command tested `http://localhost:8081/health` and exited almost immediately. Compose publishes that port on IPv4 `127.0.0.1`; on the Linux runner, `localhost` can select IPv6 `::1`. Startup timing was also possible. We changed direct checks and integration-test URLs to explicit `127.0.0.1` and added a readiness retry (`b69c518`). The subsequent hosted run printed `Direct API instances: api1, api2` and moved through the rest of CI. Lesson: a retry loop cannot help when an earlier command exits the step first; locate the exact first command that ran before exit code 1.

### How we read the logs

1. Search for `##[error]`, test failures, and the last `##[group]Run ...` before the error. That identifies the failing *step*.
2. Read the commands at the start of that step and its last few output lines. In both smoke failures, this was more useful than the much longer container dump.
3. Treat `docker compose logs` after a failed step as diagnostic context, not automatically as the primary failure. Startup 502s, nginx's read-only-config message, and Npgsql's optional `libgssapi_krb5.so.2` warning did not by themselves stop the final run.
4. Reproduce the smallest relevant path locally: direct health URLs, two-instance WebSocket test, convergence workload, or browser drag. Keep the original acceptance test unchanged when fixing an overload bug.
5. Verify the fix at the correct level, commit it, then ask the user to push. Only the pushed GitHub Actions run can establish that Linux CI is green.

## Last hosted verification (Phases 0–3)

The most recent supplied `temp-ref/ci.logs.txt` is a passing GitHub Actions run on 2026-09-26. It shows:

```text
npm ci: 0 vulnerabilities
Vite production build: passed
Client state tests: 3 passed
.NET/PostgreSQL tests: 19 passed
Direct API instances: api1, api2
Cross-instance SignalR test: 1 passed
20-client/200-operation convergence test: passed
Playwright browser tests: 2 passed
```

The workflow then ran `docker compose down` as planned. The Node/action deprecation warning during post-job cleanup was not a test failure. No AWS resources are deployed automatically.

## Phase 4 — presence (local verification, 2026-09-27)

The goal was to show which members are online, their colours and cursors, and who is editing a card. A browser tab closing without warning must disappear within 30 seconds; an orphaned Redis entry must disappear even if the API never gets a disconnect callback.

We added a Redis sorted set of connection IDs scored by last heartbeat, a metadata hash for nickname/colour/editing card, and a board registry for the sweep. The SignalR hub registers presence on join, sends an initial snapshot, broadcasts changes and cursors through the existing Redis backplane, and removes presence on disconnect. Multiple tabs have separate connection IDs; the UI groups them by member ID. The client sends a heartbeat every ten seconds, throttles pointer messages to at most one every 50 ms (about 20/s), and does not store coordinates in Redis or PostgreSQL. The join validates membership in PostgreSQL; subsequent high-frequency cursor messages validate the token against that connection's joined identity without a database read. Reconnect re-joins and revalidates.

The architecture sketch originally said a 30-second expiry with a 15-second sweep. Worst case, that could leave an orphan visible for roughly 45 seconds, contradicting the phase gate. We changed it to 20-second expiry and five-second sweep: worst-case removal is about 25 seconds after the last heartbeat. The sweep's Lua script rechecks the score before deletion, so a heartbeat racing the sweep wins. D14 in `architecture.md` records the decision.

Mistakes and their evidence:

1. A plain `dotnet build` tried to read the user-level NuGet config, which this sandbox cannot access. `dotnet build --no-restore` compiled successfully from the existing restore. Docker/Testcontainers calls also needed explicit permission for the Docker named pipe. These were environment restrictions, not app failures.
2. The first generic Testcontainers Redis test did not compile: `UntilPortIsAvailable` is not a method in the installed version. We changed it to the supported `UntilMessageIsLogged("Ready to accept connections")`, rebuilt with zero warnings, and ran the test against a real Redis container.
3. The first browser experiment used Chrome DevTools `Page.crash`. The member vanished, but Playwright hung during crash/renderer teardown and hit its 30-second test timeout. We changed the browser test to close the tab without running unload handlers and added a separate Redis test that forcibly ages one member's score while keeping another fresh. This splits normal abrupt tab loss from the orphan-sweep failure mode.
4. The browser assertions then passed, but on Windows Playwright's auto-started Vite process did not exit after printing success. Starting Vite separately and letting Playwright reuse it produced a clean exit code 0. We did not count the earlier printed `ok` as a complete passing run.
5. A review found that the first cursor handler queried PostgreSQL for membership on every pointer update. That contradicted the “cursors never touch Postgres” requirement. We retained membership checked at join, saved the authorized member/token in SignalR connection context, and rechecked that context on each cursor message. A second review found that cursor messages still refreshed Redis presence on every move; we removed that write too. Only the separate heartbeat refreshes Redis. If a tab was suspended long enough to expire, a failed heartbeat asks it to re-join. The final browser run used rebuilt API containers with both corrections.

Final local evidence: `dotnet build Cardflow.slnx --configuration Release --no-restore` passed with zero warnings; `dotnet test Cardflow.slnx --configuration Release --no-build` passed 20 tests; the cross-instance SignalR test passed; `npm run build` and `npm test` passed; the 20-client/200-operation convergence test passed; all three Playwright tests passed with exit code 0 against the rebuilt two-API Compose stack. The Playwright presence test observed the live member, colour-coded cursor, editing marker, and disappearance after closing the guest tab. The Redis test proved that the sweep removes a stale orphan and leaves a fresh heartbeat intact. The browser test's fast disappearance was the disconnect path; the Redis test covers the fallback. The implementation commit is `e3d0e86`.

### Hosted Phase 4 verification

The user pushed `5ca6544` and supplied `temp-ref/ci.logs.txt`. The GitHub Actions run of 2026-09-26 19:38–19:40 UTC checked out that exact commit and passed: `npm ci` with zero reported vulnerabilities; Vite production build; three client-state tests; 20 .NET/PostgreSQL/Redis tests (including the orphan sweep); healthy direct APIs and nginx smoke checks; one cross-instance SignalR test; 20-client/200-operation convergence; and all three Playwright tests, including presence, cursor, editing marker, and tab close. The workflow then ran its planned `docker compose down`. The post-job Node.js 20 deprecation warning is from `actions/setup-node@v4` and did not fail the run. This closes the Phase 4 CI gate; it does not start Phase 5 or establish anything about AWS deployment.

## Phase 5 — visual direction gate

The user initially started Phase 5 on 2026-09-27. We read its scope before editing the UI: Tailwind pass, empty and loading states, share-link interaction, favicon, and an OG image. The phase explicitly requires showing two visual directions before committing to one. We inspected the existing React/CSS implementation and created `web/design/phase5-directions.svg` to compare a warm editorial direction and a crisp command-centre direction. Its SVG parsed successfully as XML, and we rendered `web/design/phase5-directions.png` so the user could see both themes directly in chat. No Phase 5 UI was changed or committed.

The user then explicitly paused Phase 5 to invite a graphic designer friend to design the experience first. We stopped the visual implementation and prepared `design-handoff/`: a plain-language brief, user journeys, a screen/state checklist, two verified YouTube references (Trello board basics and Figma live collaboration), and five PNGs captured from a fresh sample board. The screenshots were checked visually and labelled as current behaviour, not a design direction. No actual user board or invitation link was included. The friend can return wireframes or Figma designs; we will resume Phase 5 only when the user asks. At the user's request, we consolidated the brief into a single offline `design-handoff/index.html` and removed the Markdown copies from that handoff folder. The first offline browser check found the gallery images had not loaded yet because they were marked lazy; removing lazy loading made all five images load reliably. A second check confirmed all images and internal navigation links, with no horizontal overflow at desktop or phone width. The user does not want this handoff pushed to Git. `launchSettings.json` remains untouched.

After confirming that they had a backup, the user asked us to remove the handoff from the repository. We verified the exact paths and deleted only `design-handoff/` and `design-handoff.zip`. Both are absent from the workspace now; recovery depends on the user's backup because these untracked copies were not committed. Phase 5 remains paused.

## Next update rule

For every new phase or meaningful fix, append: the goal, the smallest test or observation used, the first failure and its evidence, the change, the command and output that verified it, the commit, and any manual user action. Mark a claim “local only” until the corresponding hosted CI or deployment evidence exists. Keep `temp-ref/` ignored; summarize its useful evidence here without copying secrets or entire logs.
