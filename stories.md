# Cardflow — Features and Stories

> Short task + acceptance criteria (AC) for each story. Copy each story into GitHub Projects as one item.
> **Done** = every AC checked, tests pass in CI, and you ran it yourself.
> Design: **canvas** = [Cardflow v2 Screens](https://claude.ai/artifact/EMEXmxWSgYpWUt3zxvMPiA) (artboard named in each story) · **Figma** = [Card flow 2](https://www.figma.com/design/R9GNkviHAJtm3fzF1PykpO/Card-flow-2).
> Rules behind the stories: `architecture.md`. Invariants are written I1–I7.
> Build one feature at a time: agree the request/response and hub message shapes first, then its BE and FE stories together.

---

## F0 — Walking skeleton

Every layer running locally, nothing useful yet.

**BE-0.1 Solution skeleton**
Task: .NET 10 solution (Api, Application, Domain, Infrastructure, Tests) with `GET /health`.
- [ ] `dotnet build` and `dotnet test` pass on a clean clone
- [ ] `/health` returns 200 with Postgres and Redis status, plus the instance name

**BE-0.2 Local stack**
Task: Docker Compose with Postgres, Redis, `api-1`, `api-2`, nginx, Mailpit (catches emails locally).
- [ ] `docker compose up` works on a clean machine
- [ ] Repeated `/health` calls through nginx show both instance names
- [ ] Stop `api-1` → `/health` through nginx still returns 200
- [ ] Mailpit's web page opens on localhost

**BE-0.3 Database and real-database tests**
Task: First EF Core migration. One integration test using Testcontainers.
- [ ] The test starts a real Postgres and passes
- [ ] Migrations run on startup in Development only

**BE-0.4 Cross-instance ping**
Task: SignalR hub `/hubs/board`, WebSockets only, Redis backplane. A `Ping` reaches every client.
- [ ] Automated test: a client on `api-1` receives a ping sent by a client on `api-2`
- [ ] Server rejects non-WebSocket transports

**BE-0.5 CI**
Task: GitHub Actions — build, test, Compose up, `/health` smoke check.
- [ ] Green on push
- [ ] A failing test fails the run

**FE-0.1 React skeleton and theme**
Task: Vite + React + TypeScript + Tailwind. Theme tokens and fonts from `architecture.md` §5. Black header with logo.
Screen: canvas › *Board — with cards* (header only)
- [ ] `npm run dev` shows the header in Space Grotesk
- [ ] All colours come from one tokens file
- [ ] Lint and type check run in CI

**FE-0.2 Ping page**
Task: Dev-only page with a Ping button and a list of pings received, showing which instance sent each one.
- [ ] Two browsers, routed to different instances, see each other's pings

**FE-0.3 Generated API types**
Task: Generate TypeScript types from the API's OpenAPI description.
- [ ] One command regenerates the types
- [ ] CI fails if the generated types are out of date

---

## F1 — Create and open a board

**BE-1.0 Identity plumbing and dev sign-in**
Task: `users` table, JWT access-token checking on REST and the hub, and `POST /api/dev/sign-in {email}` that issues a real access token with no code. Development only.
- [ ] Every board endpoint and hub method needs a valid token → otherwise 401
- [ ] The dev sign-in endpoint does not exist outside Development

**FE-1.0 Dev sign-in**
Task: Dev-only page: type an email, get signed in. Keep the access token in memory.
- [ ] Two browsers can sign in as two different people

**BE-1.1 Create board**
Task: `POST /api/boards {title}` creates a board, its join token and the signed-in person's admin membership.
- [ ] Title 1–60 characters, otherwise 400
- [ ] Board, admin membership and `BoardCreated` event are written in one transaction
- [ ] Response contains the board link

**BE-1.2 Board snapshot**
Task: `GET /api/boards/{token}` returns title, cards per column, members and current `seq`.
- [ ] Cards sorted by rank, then by card id
- [ ] Unknown token → 404

**FE-1.1 Landing page**
Task: "Name your board" form → create → go to the board.
Screen: canvas › *Landing — create a board*
- [ ] Empty name shows an error and sends nothing
- [ ] Success opens `/b/{token}`

**FE-1.2 Board page (read-only)**
Task: Header, the "cards · members · here now" line, four fixed columns with cards.
Screen: canvas › *Board — with cards*, *Board — empty*
- [ ] Empty board shows "Your board is ready!"
- [ ] A card's header band has its column's colour
- [ ] Card and member counts are correct

**FE-1.3 404 page**
Task: "This link doesn't work" for any unknown board link.
Screen: [Figma › Desktop 3](https://www.figma.com/design/R9GNkviHAJtm3fzF1PykpO/Card-flow-2?node-id=57-422)
- [ ] Unknown token shows the page
- [ ] "Back to start" opens the landing page

---

## F2 — Join by link

**BE-2.1 Join**
Task: `GET /preview` returns the board title only. `POST /join` adds the signed-in account as a member.
- [ ] Signed out → 401
- [ ] Joining twice → still one member
- [ ] A removed member is refused → 404 (D33)

**BE-2.2 Membership check on every request**
Task: One guard used by every board endpoint and hub method (I3).
- [ ] Not a member → 403, checked by tests on REST and on the hub

**FE-2.1 Join page**
Task: Board title, "You'll join as {username}", Join button. A signed-out visitor signs in first and comes back here.
Screen: canvas › *Join this board*
- [ ] An existing member skips this page and lands on the board
- [ ] After sign-in the visitor returns to this exact board

---

## F3 — Cards: create, edit, delete

**BE-3.1 Create card**
Task: New card at the top of its column, with the next card number for that board.
- [ ] 20 parallel creates → 20 different numbers
- [ ] Card and event written in one transaction

**BE-3.2 Edit card text**
Task: Update title and description, checked against `text_version`.
- [ ] Old version → 409 with the current text and version
- [ ] Success increases the version by 1

**BE-3.3 Delete card**
Task: Mark the card deleted.
- [ ] Deleted card is gone from the snapshot
- [ ] Editing a deleted card → 410 "This card was deleted"

**FE-3.1 Add a card**
Task: Header button adds to the top of Ideas. A column's button adds to the top of that column.
Screen: canvas › *Board — with cards*
- [ ] New card appears at the top of the right column
- [ ] Empty title is not sent

**FE-3.2 Card details window**
Task: Open a card. Edit title and description. Saves when the field loses focus.
Screen: canvas › *Card details*
- [ ] Esc and the X close it
- [ ] Shows the card number and the created and updated dates

**FE-3.3 Delete card**
Task: Confirm dialog, then delete.
Screen: canvas › *Delete card — confirm*
- [ ] Cancel changes nothing
- [ ] Confirm removes the card and closes the window

---

## F4 — Move cards

**BE-4.1 Rank generator**
Task: A pure function that returns a rank between two neighbours (`architecture.md` §7).
- [ ] Unit tests: empty column, before first, after last, between two
- [ ] 1,000 inserts into the same gap stay in order
- [ ] Ranks contain only `0` and `1` and end in `1`

**BE-4.2 Move card**
Task: Takes the neighbour card ids. Server computes the rank. Checked against `position_version`.
- [ ] A normal move updates exactly one card row
- [ ] Equal ranks sort by card id
- [ ] Old `position_version` → 409

**BE-4.3 Rank sweep**
Task: Background job, once a minute, spreads ranks out again when one gets longer than 50 characters.
- [ ] Card order is the same before and after the sweep

**FE-4.1 Drag and drop**
Task: dnd-kit, within a column and across columns.
Screen: canvas › *Board — with cards*
- [ ] Sends the neighbour ids of where the card was dropped
- [ ] Works with the keyboard

**FE-4.2 Status dropdown**
Task: Changing Status in the card window moves the card to the bottom of that column.
Screen: canvas › *Card details*
- [ ] Card appears at the bottom of the new column

---

## F5 — Real-time sync

**BE-5.1 Event log**
Task: Every change writes a `board_events` row with the next `seq`, in the same transaction.
- [ ] Concurrency test: `seq` per board has no duplicates and only goes up

**BE-5.2 Board groups and broadcast**
Task: `JoinBoard` adds the connection to the board's group. Every change is broadcast with its `seq`.
- [ ] Test with two instances: a change made on one reaches a client on the other (I6)

**BE-5.3 Changes through the hub**
Task: Hub methods call the same services as REST. No rules inside the hub.
- [ ] Membership is checked on every message (I3)
- [ ] Each service is tested without a socket

**FE-5.1 Live connection**
Task: SignalR client, WebSockets only, negotiation skipped. Apply incoming events in `seq` order.
Screen: canvas › *Board — with cards* ("Live" pill)
- [ ] A change in browser A shows in browser B within 1 second

**FE-5.2 Instant moves with rollback**
Task: Show a move at once. Undo it if the server rejects it.
- [ ] Rejected move snaps back and shows a short message
- [ ] With the API stopped, a drag rolls back

---

## F6 — Reconnect and catch-up

**BE-6.1 Catch-up**
Task: `CatchUp(lastSeq)` returns the missed events, or a fresh snapshot if more than 500 behind.
- [ ] Test: 50 behind → 50 events
- [ ] Test: 600 behind → snapshot

**BE-6.2 Event pruning**
Task: Job deletes events older than 7 days or beyond the newest 5,000 per board.
- [ ] Old rows removed, newest kept

**FE-6.1 Reconnect banner and recovery**
Task: Show "Reconnecting…", then catch up without a page refresh.
Screen: canvas › *Board — reconnecting*
- [ ] Stop the API → banner appears within 5 seconds
- [ ] Start it again → board matches a fresh load, with no refresh (I5)

**FE-6.2 Gap detection**
Task: A live event whose `seq` is not the next one triggers a catch-up.
- [ ] Unit test on the board state: a gap causes a catch-up call

---

## F7 — Card details: due date and assignees

**BE-7.1 Due date**
Task: Set or clear the due date, checked against `due_version`.
- [ ] Old version → 409 with the current date

**BE-7.2 Assignees**
Task: Add and remove an assignee. No version check: adding twice is the same as adding once.
- [ ] Add twice → assigned once, no error
- [ ] Any board member can be assigned, online or not

**FE-7.1 Due date and assignees**
Task: Edit both in the card window. Show them in the card's footer.
Screen: canvas › *Card details*
- [ ] Footer shows assignee initials and the date as "OCT 12"

---

## F8 — Subtasks

**BE-8.1 Add, rename, delete subtask**
Task: Subtasks listed in the order they were added (UUIDv7 ids). Renaming is checked against the subtask's version.
- [ ] New subtask goes to the end
- [ ] Old version on rename → 409

**BE-8.2 Tick a subtask**
Task: The command says "set done = true or false", never "toggle".
- [ ] Sending the same command twice gives the same result

**FE-8.1 Subtasks panel**
Task: List, add, rename, tick, delete. Progress bar. "2/3" on the card's footer.
Screen: canvas › *Card details*
- [ ] Progress and footer count update as you tick

---

## F9 — Edit conflicts

**BE-9.1 Conflict replies**
Task: Every 409 carries which part clashed, its current value and its version.
- [ ] Test: one client ticks a subtask while another changes the due date → both succeed
- [ ] Test: two clients rename from the same version → the second gets 409

**FE-9.1 "Someone else changed this"**
Task: Show the other person's version, keep your own text, offer "Copy my text".
Screen: canvas › *Card details — edit conflict*
- [ ] Your text is never silently lost (I4)
- [ ] "Got it" closes the message

---

## F10 — Search

**FE-10.1 Search the board**
Task: Filter cards already loaded in the browser by title, description, subtask text and assignee name.
Screen: canvas › *Board — with cards* (search bar)
- [ ] Results update as you type, with no server call
- [ ] Dragging is turned off while a search is active, and the Status dropdown still works

---

## F11 — Presence

**BE-11.1 Who is here**
Task: Redis sorted set per board. Heartbeat every 10 seconds, sweep every 5, expire after 20.
- [ ] A browser closed without warning disappears within 30 seconds

**BE-11.2 Editing markers**
Task: "X is editing" marker per card, 20-second expiry, refreshed by heartbeat.
- [ ] Marker clears when the card window closes

**BE-11.3 Cursor relay**
Task: Pass cursor positions to the board group. Never stored.
- [ ] No cursor data reaches Postgres or Redis

**FE-11.1 Here-now list**
Task: Avatars in the header and name pills in the "here now" line.
Screen: canvas › *Board — with cards*
- [ ] Join and leave show up in other browsers

**FE-11.2 Live cursors**
Task: Other people's cursors with name tags. Send at most 20 positions per second.
Screen: canvas › *Board — with cards* (Lin's cursor)
- [ ] Cursor follows in the other browser

**FE-11.3 Editing marker**
Task: "Lin editing" tag on the card and in the card window.
Screen: canvas › *Board — with cards*, *Card details*
- [ ] Shows while someone has the card open

---

## F12 — Share

**FE-12.1 Share dialog**
Task: Show the board link with a Copy button.
Screen: [Figma › Desktop 7](https://www.figma.com/design/R9GNkviHAJtm3fzF1PykpO/Card-flow-2?node-id=159-581)
- [ ] Copy puts the link on the clipboard and shows "Link copied"

---

## F13 — Email sign-in

**BE-13.1 Send a code**
Task: `POST /api/auth/code {email}` emails a 6-digit code. Locally the email lands in Mailpit.
- [ ] Code expires after 10 minutes; a new code replaces the old one
- [ ] Only the code's hash is stored
- [ ] 1 send per minute and 5 per hour per email, 20 per hour per IP → 429
- [ ] Same reply whether or not the email has an account

**BE-13.2 Verify the code**
Task: `POST /api/auth/verify {email, code}`. The first success creates the account. Returns tokens (F14).
- [ ] A code works once only
- [ ] 5 wrong tries → the code is dead
- [ ] Reply says whether a username still needs to be set
- [ ] Codes and emails never appear in logs

**BE-13.3 Username**
Task: `PUT /api/me/username`. Required after the first sign-in, changeable any time.
- [ ] 1–24 characters, does not have to be unique
- [ ] Until it is set, every other endpoint returns 403 "username required"
- [ ] A change shows live on every board the person is on

**BE-13.4 Retire the dev sign-in**
Task: Real sign-in replaces the dev sign-in from BE-1.0 everywhere except local development.
- [ ] Production build: `/api/dev/sign-in` → 404
- [ ] All tests that need a person use either the test auth handler or the real code flow

**FE-13.1 Email and code screens**
Task: Step 1 asks only for the email. Step 2 asks for the code.
Screen: canvas › *Account — email code sign-in* (steps 1 and 2)
- [ ] The code field accepts paste and phone autofill (`autocomplete="one-time-code"`)
- [ ] "Send a new code" stays off until the 60-second timer ends
- [ ] "Use a different email" goes back to step 1
- [ ] After sign-in you go back to where you started

**FE-13.2 Username box**
Task: Pops up after the first sign-in. Also opened later from the account menu.
Screen: canvas › *Account — email code sign-in* (first sign-in)
- [ ] Cannot be closed on the first sign-in until a username is saved

**FE-13.3 Signed-in state**
Task: Header shows the username, with Change username and Sign out. "Create your board" sends a signed-out person to sign in first.
Screen: canvas › *Landing — create a board*
- [ ] The board name typed before signing in is kept

---

## F14 — Sessions: access and refresh tokens

**BE-14.1 Issue tokens**
Task: Access token = JWT, 15 minutes, in the reply body. Refresh token = 32 random bytes, 30 days, in an HttpOnly, Secure, SameSite=Strict cookie on path `/api/auth`.
- [ ] Only the refresh token's hash is stored
- [ ] The access token holds who you are, never board membership

**BE-14.2 Refresh with rotation**
Task: `POST /api/auth/refresh` kills the old refresh token and issues a new pair.
- [ ] Expired, unknown or revoked refresh token → 401 (sign in with a code again)
- [ ] Sending an already-used refresh token revokes its whole chain

**BE-14.3 Tokens on the hub**
Task: Read the access token from the `access_token` query value, for `/hubs/board` only. Close the connection when the token expires (`CloseOnAuthenticationExpiration`).
- [ ] Logs never contain query strings with tokens
- [ ] Test: token expires → connection closed

**BE-14.4 Sign out**
Task: Sign out revokes the current chain. "Sign out everywhere" revokes all of the account's chains.
- [ ] After sign-out, refresh → 401

**FE-14.1 Tokens in the browser**
Task: Access token in memory only. Refresh on page load. One refresh at a time. Retry a 401 once after refreshing.
- [ ] Reloading the page keeps you signed in
- [ ] Two requests failing together trigger only one refresh
- [ ] Refresh fails → "Your session ended" → sign-in screen → back to the same board

**FE-14.2 Hub reconnect on expiry**
Task: When the hub closes for an expired token, refresh and reconnect with the new token.
Screen: canvas › *Board — reconnecting*
- [ ] Leaving a board open for an hour gives no visible break, and no missed changes

---

## F15 — Admin actions

**BE-15.1 Delete board**
Task: Creator only. Hard delete in one transaction. Broadcast `BoardDeleted`.
- [ ] Anyone else → 403 (I7)
- [ ] Every open browser on the board moves to the 404 page

**BE-15.2 Remove a member**
Task: Mark the member removed. Close their connections on every instance. They cannot rejoin (D33).
- [ ] Their next message is rejected (I3)
- [ ] Opening the board link again → 404
- [ ] Test: works when they are connected to the other instance

**BE-15.3 Reset the link**
Task: New join token. Every member except the admin loses access until they open the new link.
- [ ] Old link → 404
- [ ] The admin keeps access
- [ ] Rejoining with the new link brings back the same member, with their assignments

**FE-15.1 Board settings**
Task: Gear button, shown only to the admin. Members list with Remove, Reset link, Delete board.
Screen: canvas › *Board settings (admin)*
- [ ] Gear button hidden for other members

**FE-15.2 Delete board confirm**
Task: The admin types the board name to turn on the Delete button.
Screen: canvas › *Delete board — confirm (admin)*
- [ ] Button stays off until the name matches exactly

**FE-15.3 Removed or deleted**
Task: A removed member, or anyone on a deleted board, lands on the 404 page.
Screen: [Figma › Desktop 3](https://www.figma.com/design/R9GNkviHAJtm3fzF1PykpO/Card-flow-2?node-id=57-422)
- [ ] Happens within 2 seconds, without a refresh

---

## F16 — Finish the product

**FE-16.1 Loading and error states**
- [ ] Every screen has a loading state and an error state

**FE-16.2 Phone layout**
- [ ] At 390px wide the columns stack, nothing scrolls sideways, and buttons are at least 44px

**FE-16.3 Favicon and link preview**
- [ ] A shared link shows the title and an image in chat apps

---

## F17 — Prove it

**BE-17.1 Convergence test**
Task: 20 clients, 200 random changes, then compare every client's board (I1).
- [ ] All 20 boards are identical, in CI

**BE-17.2 Load test**
Task: k6 with 500 WebSocket connections across 25 boards.
- [ ] The p95 delay from one client to another, and the breaking point, are written in the README

**BE-17.3 Observability**
Task: OpenTelemetry logs, metrics and traces. Metrics from `architecture.md` §18.3.
- [ ] `RejectedWrites` per part and `RankCollisions` are visible locally

**FE-17.1 End-to-end test**
Task: One Playwright run: two browsers, drag a card, then a rename conflict.
- [ ] Passes in CI

**DOC-17.1 README**
- [ ] Demo GIF, architecture diagram, how fractional ranks and `seq` catch-up work, and the load-test numbers

---

## F18 — Deploy `[pending]`

Hosting is not chosen: AWS (built and destroyed on demand) or an Oracle Cloud Always Free server with Coolify. Stories come once it is chosen.
