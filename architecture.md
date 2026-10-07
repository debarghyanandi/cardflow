# Cardflow — Real-Time Collaborative Board

> **Name:** Cardflow. Chosen before the first commit for the repo, namespace and URL.

> **Written by:** Claude, for Debarghya. **Built by:** Debarghya, by hand. AI reviews; it does not write the code.
> **Authoritative copy:** this file, in the root of the build repo.
> **Design:** Figma "Card flow 2" — https://www.figma.com/design/R9GNkviHAJtm3fzF1PykpO/Card-flow-2 is the original design. The screens added or changed for v2 live on the **"Cardflow v2 Screens" canvas** — https://claude.ai/artifact/EMEXmxWSgYpWUt3zxvMPiA — because the Figma account has a view-only seat.
> **Last updated:** 2026-10-07 — v2.3. Everyone signs in — email + one-time code, then access and refresh tokens; username set on first sign-in. No guests. Hosting pending; sign-in built after the core. Stories live in `stories.md`.

---

## 0. How to use this file

**Read this before writing any code. Re-read it from the repo before editing it.**

**Tags:**
- `[decided]` — agreed. Change it only by adding an entry in §13 and saying why.
- `[proposed]` — current best idea, open to argument.
- `[open]` — not decided yet.
- `[pending]` — known problem, parked on purpose. Not forgotten.

**Rules for anyone working here — me, or an AI reviewing my code:**

1. **Nothing is done until it has run.** A test passes in the repo, or the output was pasted. A commit message saying "works" is not proof.
2. **Do not break an invariant in §3.** If one seems wrong, stop and raise it.
3. **This is a portfolio project.** The demo and the README are deliverables, not an afterthought. A feature nobody can see in 60 seconds is worth less than one they can.
4. **Plain English in every document.** Short sentences. Keep real technical terms, and explain each one the first time it appears.
5. **I write the code.** An AI may explain, review and point at problems. It does not hand me finished files.

### What changed from v1

v1 was built up to Phase 4 (presence), then the repo was deleted to rebuild by hand. The core design survives: server authority, fractional ranks, the event log with `seq`, the Redis backplane, two instances from day one. What is new:

| v1 | v2 |
|---|---|
| Users add, rename and order their own columns | **Four fixed columns:** Ideas · Doing · Review · Done |
| A card is a title and a description | A card also has **assignees, a due date, subtasks** and a **card number** |
| One `version` per card | **Each part of a card has its own version** (§7) |
| No accounts at all | **Everyone has an account** — email + one-time code, no password. No guests |
| Everyone is equal | **Two roles:** admin (the creator) and user |
| No search | **Search** filters the loaded board in the browser |
| Plain CSS, visual design later | **Visual design exists** (Figma) and is built from the first feature |

---

## 1. What it is

A shared board with four columns — **Ideas, Doing, Review, Done** — and cards, like a small Trello. Several people open the same board at once. One person drags a card, and everyone else sees it move immediately.

Each card has a title, a description, assignees, a due date, a checklist of subtasks, and a number like `#101`.

Each person sees the others: a coloured badge with a name, a live cursor, and a marker on the card someone is editing.

**Creating a board needs an account** — just an email. CardFlow emails a 6-digit code; there is no password. The creator becomes the board's admin. **Joining a board needs the same sign-in** — open the link, sign in with an emailed code if needed, press Join. A visitor without an account can use any inbox, such as a public Mailinator address.

**The pitch, in one sentence:** a collaborative board where the interesting part is not the board — it is what happens when two people touch the same card at the same moment.

---

## 2. The one hard problem

**Two people change the same thing at the same time, and everyone must end up seeing the same board.**

That has a name. **Convergence** — after all messages are delivered, every client shows identical state. Not "roughly the same". Identical.

It shows up in five places.

**Ordering.** Cards in a column have an order. Two people drop a card into the same gap at the same time. If order is an integer position, both writes say "position 3". Either one silently takes the other's place, or the server renumbers every card below. Neither is acceptable.

**Concurrent edits to one card.** Two people rename the same card. Somebody's text has to lose. The real question is whether the loser finds out.

**One card, many parts.** New in v2. Ada ticks a subtask while Lin changes the due date of the same card. They did not really collide — they touched different things. The design must let both writes succeed, and still catch a real collision when two people edit the same text.

**Reconnects.** A laptop sleeps for ten minutes. It wakes holding a board that is now wrong. It must catch up without downloading everything again, and without showing a half-updated board while it does.

**More than one server.** Two browsers open the same board but land on different server instances. A message sent to one must reach the other. In-memory state does not survive this, and it is the failure that only appears in production.

---

## 3. Invariants — the rules that must always hold

A feature that breaks one of these is a bug, not a trade-off. Each one gets an automated test.

| # | Invariant | Status |
|---|---|---|
| I1 | **Convergence.** After all messages are delivered, every connected client shows the same card order and the same card contents — every part, including subtasks and assignees. | `[decided]` |
| I2 | **The server is the only authority.** A client may predict a change and show it at once, but the server's version always wins. A client never writes to another client. | `[decided]` |
| I3 | **Authorization on every message, not just on connect.** Access is checked when a message arrives, not only when the socket opens. A removed member, or a member whose link was reset, is rejected on their next message. | `[decided]` |
| I4 | **No lost change.** A card or subtask can be moved, edited or deleted. A change is never silently dropped because two people touched it at once — either it is applied, or its sender is told. | `[decided]` |
| I5 | **Reconnect is exact.** A client that reconnects lands on the same state as one that never disconnected. No page refresh needed. | `[decided]` |
| I6 | **Works across server instances.** Every behaviour above holds with two or more servers behind a load balancer. | `[decided]` |
| I7 | **Roles are enforced by the server.** Admin-only actions (delete board, remove member, reset link) are checked in the service layer. Hiding a button in the browser is not security. | `[decided]` |

**I6 is the one that gets skipped.** Everything works on one machine. Run two instances locally from day one so it cannot be skipped.

---

## 4. Scope

### In

- **Board:** create, join by link, share the link, delete (creator only) — all signed in
- **Four fixed columns:** Ideas · Doing · Review · Done. Created with every board. Not renamed, reordered or deleted
- **Cards:** create, edit title and description, move by drag, move by the Status dropdown, delete
- **Card details:** assignees, due date, subtasks (add, rename, tick, delete), card number, created and updated times
- **Search:** filters the loaded board in the browser
- **Real-time** updates over WebSockets
- **Live presence:** who is here, their cursor, who is editing which card
- **Accounts:** everyone signs in with an emailed code, sets and changes a username, signs out (§8)
- **Roles:** admin and user. Admin can delete the board, remove a member, and reset the link
- Optimistic updates on the client, with rollback when the server disagrees
- Reconnect with catch-up
- Two or more server instances, proven
- A load test with a published number
- **Deployment** `[pending]`: AWS built and destroyed on demand, or an Oracle Cloud Always Free server with Coolify (§18)
- Dashboards, alarms and log queries on whichever host is chosen

### Cut `[decided]`

| Cut | Why |
|---|---|
| Guest access (no account) | One identity system instead of two. A visitor uses any inbox, e.g. Mailinator (D32) |
| Google or other OIDC sign-in | Replaced by email codes (D29) |
| Passwords, password reset | No passwords at all (D29) |
| User-made columns | Four fixed columns (D16). Removes column ranks and a whole class of conflicts |
| Category tags on cards | In the Figma design, removed for v2 (D20). Coming back later — see Deferred |
| A separate Notes field | Merged into Description (D21). Two free-text boxes on one card is one too many |
| Voice search (the mic button) | Browser support is patchy and it adds nothing to the hard problem (D22) |
| Full CRDT or operational transform | Correct, and weeks of work. §7 gives a cheaper design that is honest about its limits |
| Rich text inside cards | A text editor is its own hard problem. Plain text only |
| File attachments | Upload pipeline, storage, scanning. A different project |
| Mobile app | The web page is responsive. That is enough |
| Notifications, email, digests | Invisible in the demo |

### Deferred — only after the core works

- **Category tags** — user-created, with colours (the original Figma design has them)
- Reordering subtasks by drag
- Card comments
- Undo
- Export to JSON or CSV
- Transferring the admin role

---

## 5. Screens `[decided]`

The visual design lives in Figma (link in the header). Build the UI to match it.

### The look

Bright and flat, with hard black shadows. A yellow page, black header, pastel columns, white cards with thick black borders.

**Fonts** (all on Google Fonts):

| Font | Used for |
|---|---|
| **Space Grotesk** | Almost everything. Regular, Medium and Bold |
| **Archivo Black** | The big landing-page headline |
| **Space Mono** | Board links, avatar letters, "3 here now" |

**Colours:**

| Token | Hex | Used for |
|---|---|---|
| `page` | `#F8EB9B` | Page background |
| `ink` | `#000000` | Header, borders, hard shadows, text |
| `paper` | `#FFFFFF` | Cards, dialogs |
| `ideas` · `ideas-strong` | `#FFC2E0` · `#F486BC` | Ideas column header · its "Add a card" button |
| `doing` · `doing-strong` | `#B9CDFF` · `#80A4FC` | Doing column |
| `review` · `review-strong` | `#FFF4B8` · `#FFE066` | Review column. `review-strong` is `[proposed]` — the design had no Review column |
| `done` · `done-strong` | `#BFF5DF` · `#5AD9A6` | Done column, "Live" pill, dialog headers |
| `lavender` | `#E6DBFF` | Squiggle decoration, accents |
| `danger` | `#FF6B6B` | Delete buttons, the 404 numbers `[proposed]` |

**A card's header is filled with its column's colour.** In v1 of the design a category tag sat there. With tags cut (D20), the header band shows the card number and takes the column colour. When a card moves, its header colour changes with it. The card modal's top band does the same.

### The screen list

| Screen | Figma | Notes |
|---|---|---|
| Landing — create a board | Page 1 · Desktop 1 | v2: the form is "Name your board" + **Create your board**; a signed-out user goes to sign in first (v2 canvas). "No account, ever" becomes "People you invite sign in the same way" |
| Account — email, code, first-time username | v2 canvas | One flow for new and returning people. The username box appears only on the first sign-in |
| Join this board | v2 canvas (replaces Page 1 · Desktop 2) | Shows the board title and "You'll join as Lin". No name field — the username is used |
| 404 — this link doesn't work | Page 1 · Desktop 3 | Used for: wrong link, deleted board, reset link, removed member |
| Board — empty | v2 canvas | Four empty fixed columns and a "Add your first card" prompt. Replaces Desktop 4 |
| Board — with cards | v2 canvas | Four columns, no tags. Replaces Desktop 5, 8 and 9 |
| Card details (modal) | v2 canvas | No category. Status has four values. Notes merged into Description |
| Share this board | Page 1 · Desktop 7 | As designed |
| Edit conflict — "Someone else changed this" | v2 canvas | Shown inside the card modal (§7) |
| Reconnecting / offline | v2 canvas | The "Live" pill turns into "Reconnecting…", plus a banner |
| Delete card — confirm | v2 canvas | |
| Board settings (admin) | v2 canvas | Members list with Remove, Reset link, Delete board |
| Delete board — confirm | v2 canvas | Admin only |

**Board header line:** `12 cards · 3 members · Here now: Ada, Lin`. "Members" is everyone who ever joined. "Here now" is live presence. (The Figma screens show "0 columns · 1 member" on a full board — a copy mistake.)

**Mobile** `[open]` — no design yet. The four columns stack, or scroll sideways. Decide in F16.

---

## 6. Architecture `[proposed]`

```
                 Browser — React + TypeScript
              REST to load · WebSocket for live changes
                    │
                    ▼
              ┌───────────────────────────────┐
              │   Load balancer (ALB / nginx) │
              └───────────────────────────────┘
                     │                  │
                     ▼                  ▼
         ┌────────────────┐    ┌────────────────┐
         │ API instance 1 │    │ API instance 2 │   ASP.NET Core (.NET 10)
         │ REST + Hub     │    │ REST + Hub     │   SignalR hub
         └────────────────┘    └────────────────┘
                     │                  │
        ┌────────────┴──────────────────┴────────────┐
        ▼                                            ▼
┌──────────────────┐                      ┌──────────────────────┐
│   PostgreSQL     │                      │        Redis         │
│  boards, members │                      │ • SignalR backplane  │
│  cards, subtasks │                      │ • presence + TTL     │
│  events, users   │                      │ • editing markers    │
│  THE TRUTH       │                      │                      │
└──────────────────┘                      └──────────────────────┘
```

### What each part does

**API (.NET).** Two doors into the same application.

- **REST** loads a board when the page opens, and handles anything not time-critical — accounts, sign-in, creating a board, joining. It returns a snapshot plus a sequence number.
- **SignalR hub** carries live changes both ways. SignalR is Microsoft's real-time library. Cardflow uses WebSockets only; the client skips transport negotiation.

Both doors call the same service layer. **There is no business logic inside the hub.** The hub receives a message, calls a service, broadcasts what the service returns. This is what makes the logic testable without a socket, and it is the single most important structural rule in the project. **Role checks (I7) live in the service layer too**, so they cannot be skipped by calling the hub instead of REST.

**PostgreSQL.** The truth. Users, boards, members, cards, subtasks, and an append-only list of everything that has happened (§9). If Redis is wiped, nothing is lost.

**Redis.** Three separate jobs. Do not confuse them with each other.

1. **SignalR backplane.** When instance 1 broadcasts to a board, Redis carries it to instance 2. This is what makes I6 possible, and it is one line of configuration.
2. **Presence.** Who is connected to which board, with a timer that removes anyone who stops responding.
3. **Editing markers.** "Lin is editing this card." Short-lived, never written to Postgres.

Cursor positions go through the hub to the group and are never stored anywhere.

**Redis is never the source of truth for board content.** If Redis is empty, the board still loads.

**React client.** Vite, TypeScript. `dnd-kit` for dragging. TanStack Query for the REST load. The official SignalR JavaScript client for live updates.

### Why not Blazor Server

Blazor Server would be less code — it already runs over SignalR and updates the page for you. Rejected because every interaction needs a server round trip, so dragging a card feels heavy on a slow connection, and every connected user holds server memory. `[decided]`

This belongs in the README. A rejected option with a reason reads as judgment; a list of chosen technologies does not.

---

## 7. Ordering and concurrent edits `[decided]`

**This is the heart of the project. Get this right and the rest is ordinary work.**

### Columns are fixed, so only cards need ordering

The four columns are a fixed list in code: `ideas`, `doing`, `review`, `done`, in that order. A card stores which one it is in as a `status` value. There is no columns table and no column rank (D16).

### Why integer positions fail

Give each card a position: 1, 2, 3. To insert between 1 and 2, every card below shifts down. Two people insert at once and they fight over the same numbers.

### Fractional ranks

Each card holds a **rank** — a short binary string, not an integer position. The string represents a fraction between 0 and 1. Generated ranks end in `1`; PostgreSQL sorts them with the `C` collation.

To place a card between two others, generate a string that sorts between their two ranks.

```
Empty column                 →  "1"    (one half)
Before "1"                  →  "01"   (one quarter)
Between "1" and "11"        →  "101"  (five eighths)
```

There is always another midpoint between two distinct generated ranks. This guarantee depends on using the canonical binary format; it is false for arbitrary strings. A normal insert therefore writes exactly one card row. Nothing else moves.

This is a small fractional-indexing scheme. Trello's LexoRank solves the same ordering problem with a different encoding.

**The browser sends neighbour IDs, the server computes the rank** (D12). The browser says "put card 7 between card 3 and card 9". The server works out the rank.

### Where a card lands `[decided]`

| Action | Lands |
|---|---|
| Header "+ Add a card" | Top of **Ideas** |
| A column's "+ Add a card" | Top of that column |
| Drag and drop | Where it was dropped |
| Status dropdown in the card modal | Bottom of the new column |

### The collision, and the tie-break

Two people drop a card into the same gap at the same moment. Both compute a rank between the same neighbours. Both can get `"101"`.

That is fine, and it must be handled on purpose: **when two ranks are equal, sort by card id.** The order is then stable and identical everywhere, which is invariant I1. It may not be the order either person expected, but both see the *same* order, and one small drag fixes it.

**Do not try to prevent the collision.** Preventing it needs a lock on the whole column, and that is a far worse trade.

### Rank exhaustion

Inserting into the same gap over and over makes the strings grow. A background sweep checks once a minute and redistributes the ranks of a column's cards when any active card rank passes 50 characters. The sweep preserves the existing order and retries if a concurrent write conflicts.

### Card content — one card, separate parts, each with its own version `[decided]`

v1 gave each card a single `version`. Any write from an old version was rejected. That was fine when a card was just a title and a description. Now a card holds much more, and one version would reject writes that did not really collide.

**So a card is split into parts. Each part is checked on its own** (D17).

| Part | Holds | How a concurrent write is handled |
|---|---|---|
| **Text** | Title, description | `text_version`. A write from an old version is **rejected**. The sender is told |
| **Position** | Status (column), rank | `position_version`. Two moves of the same card at once: the second is **rejected** and rolled back |
| **Due date** | Due date | `due_version`. Same as Text |
| **Assignees** | Who is assigned | **No version needed.** "Add Ada" and "remove Lin" are set operations. Adding Ada twice still leaves Ada assigned once. The result is the same whatever order they arrive in |
| **Each subtask** | Its text, its done flag | Text: own `version`, rejected if old. Done flag: the command says **"set done = true"**, never "toggle", so the last write wins and everyone sees the same result |
| **Existence** | Deleted or not | Delete beats everything. An edit to a deleted card is rejected: *"This card was deleted."* |

> *Optimistic concurrency* — the write is allowed to proceed assuming nobody else touched the row, and the assumption is checked at the moment of writing.

> *Idempotent* — doing it twice has the same effect as doing it once. "Set done = true" is idempotent. "Toggle" is not.

**The rule behind the table:** use a version check only where two people can really lose each other's work — free text and position. Where the operation can be written so that order does not matter, write it that way and skip the check.

**What the loser sees.** The card modal shows the newer value plus a small note: *"Someone else changed this."* Their own text is kept in the box so they can copy it. This never loses data silently, which is I4.

**Ada ticks a subtask while Lin changes the due date:** different parts, both succeed. **Ada and Lin both rename the card:** same part, the second is rejected and told. This contrast is the demo (§16).

### Subtask order

Subtasks are listed in the order they were added. New ones go at the end. No dragging (D19). Each subtask's id is a **UUIDv7** — an id that starts with a timestamp, so sorting by id sorts by creation time. Two subtasks added in the same millisecond still sort the same way everywhere (I1).

### Card numbers

Each card gets a board-local number: 101, 102, 103… The board row holds `next_card_number`. Creating a card runs `UPDATE boards SET next_card_number = next_card_number + 1 … RETURNING` in the same transaction as the card insert. This briefly locks the board row, so card creation on one board goes one at a time. At this scale that is fine. The prefix shown before the number is `[open]` (Q10).

---

## 8. Identity and roles `[proposed]`

### One kind of person (D32)

Everyone has an account. Creating a board and joining one both need the same emailed-code sign-in.

| | |
|---|---|
| How they get in | Email → 6-digit code |
| What identifies them | Account id in the access token |
| Role on boards they create | **admin** |
| Role on boards they join | **user** |
| Name shown on boards | Their username, live — a change shows everywhere |
| Duplicates | One account is one member on a board, never two. Usernames need not be unique |

**Demo visitors** use any inbox they can read. A public Mailinator address works. Anyone who knows that address can read its emails, so such an account is only for demo boards.

### Sign-in with an emailed code (D29)

One flow for new and returning people:

```
Enter email → POST /api/auth/code → email with a 6-digit code
Enter code  → POST /api/auth/verify
   → unknown email? create the account
   → access token in the reply, refresh token in a cookie (below)
   → no username yet? the username box must be filled before anything else
```

> *OTP (one-time password)* — a short code that works once and expires quickly. Here it replaces the password completely.

| Rule | Value |
|---|---|
| Code | 6 digits from a secure random generator. Stored only as a hash |
| Lifetime | 10 minutes. A new code replaces the old one. Works once |
| Wrong tries | 5 per code, then the code is dead |
| Send limits | 1 per minute and 5 per hour per email; 20 per hour per IP → 429 |
| Reply to "send code" | The same whether or not the email has an account |
| Email address | Stored lower-case. Never shown to other members |
| Username | Required after the first sign-in. 1–24 characters, not unique, change any time |

> *Hash* — a one-way scramble. The server can check a typed code against it, but cannot turn it back into the code. A database leak does not leak live codes.

**Email delivery.** Locally: **Mailpit**, a fake mail server in Docker Compose that catches every email and shows it in a web page. Nothing real is sent. In production: a sending service, chosen with the host (Q15).

### Access and refresh tokens (D30)

| | Access token | Refresh token |
|---|---|---|
| What | A signed JWT: account id, username | 32 random bytes. Means nothing by itself |
| Lifetime | 15 minutes | 30 days |
| Kept where (browser) | **JavaScript memory only** — never a cookie, never localStorage | HttpOnly, Secure, SameSite=Strict cookie, path `/api/auth` |
| Kept where (server) | Nowhere — checked by its signature | Only its hash, in `refresh_tokens` |
| Sent with | Every API call (`Authorization: Bearer`) and the hub connection | Only `POST /api/auth/refresh` |

> *JWT (JSON Web Token)* — a small signed text the server can check without a database lookup. Anyone can read it, nobody can change it.

**Rules:**
- **Page load:** the browser calls `/api/auth/refresh` to get an access token, since memory is empty after a reload.
- **Rotation:** every refresh kills the old refresh token and issues a new pair.
- **Reuse means theft:** if an already-used refresh token comes back, the whole chain it belongs to is revoked. Whoever holds it must sign in with a code again.
- **Refresh expired, unknown or revoked → 401 → sign in with an emailed code again.**
- **Sign out** revokes the current chain. "Sign out everywhere" revokes all of the account's chains.
- **Membership is not in the token.** It is checked in the database on every message (I3). A token says *who* you are, never *what you may touch*.

**Tokens and the WebSocket.** Browsers cannot set headers on a WebSocket, so SignalR sends the access token as the `access_token` query value. The server reads it only for `/hubs/board`, and the logs must never record query strings. With `CloseOnAuthenticationExpiration`, the hub closes a connection when its token expires. The client refreshes and reconnects, and the normal catch-up (§10) fills any gap. So the reconnect path runs every 15 minutes in normal use, which keeps it honest.

**Why not a plain cookie session?** For one browser app on one domain, a cookie session is simpler. Tokens were chosen on purpose (D30): they are the pattern most job descriptions ask about, and they make the WebSocket expiry problem real instead of hidden.

### Roles

There are exactly two roles, stored per board on the membership row: **admin** and **user**.

| Action | user | admin |
|---|---|---|
| Everything on cards and subtasks | ✅ | ✅ |
| Share the link | ✅ | ✅ |
| Remove a member | — | ✅ |
| Reset the link | — | ✅ |
| Delete the board | — | ✅ |

One admin per board: the creator. Transferring the role is deferred.

### Admin actions — what each one does `[proposed]`

**Remove a member.** Their membership is marked removed. Their open connections are dropped at once, and their next message is rejected (I3). They see the 404 screen. **They cannot rejoin, even with the link** — the join endpoint refuses an account whose membership on that board is marked removed (D33). The settings screen says this in one line. Their past assignments stay on cards, shown greyed out `[open]` (Q12).

**Reset the link.** The board gets a new join token. Old links show the 404 screen. **Every member except the admin loses access** and must open the new link to come back — this is what I3 promised in v1. Implementation: the board has a `link_generation` number; each membership records the generation it joined under; a membership from an older generation is no longer valid, except the admin's. Rejoining with the new link brings back the same member, assignments included.

**Delete the board.** A hard delete in one transaction. Everything on the board goes. Every connected client gets `BoardDeleted` and moves to the 404 screen. Boards are demo data (D8), so there is no undo and no soft delete. The confirm dialog asks the admin to type the board name first, because this is the one action that cannot be undone.

### Link format `[pending]`

The design shows `cardflow.app/b/community-picnic-8f2k`. **The four random characters are guessable.** 36⁴ is about 1.7 million, and a script tries that in hours. The link is the only lock on a board, and anyone with it can edit.

Parked on purpose for now (D23). The fix when it matters: keep the readable name, make the random part 12+ characters, and rate-limit failed joins per IP. Revisit before the app goes on a public URL.

---

## 9. Data model `[proposed]`

Not a schema. The shape.

| Table | Holds |
|---|---|
| `users` | People with accounts. Id, email (lower-case, unique), username, created time |
| `login_codes` | Email, code hash, expires at, tries left, used flag |
| `refresh_tokens` | Token hash, account id, chain id, expires at, replaced by, revoked at |
| `boards` | Id, title, slug, join token, `link_generation`, `next_card_number`, created by (user id), created time, last activity time |
| `board_members` | One account on one board. Board id, user id (unique together), colour, role (`admin` / `user`), `link_generation` joined under, removed flag |
| `cards` | Board id, number, `status` (`ideas` / `doing` / `review` / `done`), rank, title, description, due date, created by, created time, updated time, `text_version`, `position_version`, `due_version`, deleted flag |
| `card_assignees` | Card id, member id. Primary key on both — adding twice is a no-op |
| `subtasks` | Id (UUIDv7), card id, text, done, `version` |
| `board_events` | **Append-only.** Board id, `seq` (per board), type, payload (JSONB), who did it, when |

> *Append-only* — rows are added, never changed or deleted by normal application code.

**Why `status` and not a columns table:** the columns are fixed (D16). A lookup table would add a join to every query and protect nothing.

### `board_events` is what makes reconnect work

Every change writes two things in **one transaction**: the new state, and an event row describing it. If one fails, both fail.

Each event gets a `seq` — a number that only goes up, per board.

- The client holds the last `seq` it has seen.
- Live messages carry their `seq`. Receiving 48 when it expected 47 tells the client it missed one.
- On reconnect it asks for everything after its last `seq`.
- If it is more than 500 events behind, the server sends a fresh snapshot instead. Cheaper than replaying, and simpler than being clever about it.

**Event types:** `CardCreated` · `CardMoved` · `CardTextEdited` · `CardDueDateSet` · `AssigneeAdded` · `AssigneeRemoved` · `SubtaskAdded` · `SubtaskTextEdited` · `SubtaskDoneSet` · `SubtaskDeleted` · `CardDeleted` · `MemberJoined` · `MemberRemoved` · `LinkReset` · `BoardDeleted`.

Presence, cursors and editing markers are **not** events. They are live-only and not replayed.

**Events are pruned** by a minute-by-minute sweep after 7 days or beyond the newest 5,000 rows per board, whichever comes first. They are a catch-up buffer, not an audit log.

### Indexes that will matter

- `cards (board_id, status, rank)` — every board load sorts by this
- `subtasks (card_id, id)` — every card load
- `board_events (board_id, seq)` — every catch-up reads this
- `boards (join_token)` — every join hits this
- `users (email)` unique — every sign-in
- `refresh_tokens (token_hash)` unique — every refresh
- `login_codes (email, expires_at)` — every code check

---

## 10. Main flows `[proposed]`

### Create a board

```
Landing page: type a board name → "Create your board"
   → signed out? → sign-in page (or create an account) → back to the landing page
   → POST /api/boards { title }
   → one transaction: board row + admin membership + BoardCreated
   → redirect to /b/{slug-token}
```

The board name is kept in the browser while the user signs in, so they do not type it twice.

### Join a board

```
Open /b/{slug-token}
   → GET /api/boards/{token}/preview → board title only (no cards)
   → token unknown → 404 screen
   → signed out → email-code sign-in → back to this page
   → already a member (current link) → straight to the board
   → removed from this board → 404 screen (D33)
   → otherwise: "You'll join as Lin" → Join → POST /join → membership
```

### Open a board

```
GET /api/boards/{token}
   → snapshot: cards + subtasks + assignees + members + current seq
   → client renders
   → client opens the SignalR connection and joins the board group
   → client calls CatchUp with its seq; server sends missed events or a snapshot
   → live events received during catch-up are buffered and applied in seq order
```

**The gap between the snapshot and the socket opening is real.** Something can change in those 200 milliseconds. That is why the client sends its `seq` on connect and the server fills the hole. Skip this and the board is subtly wrong, and nobody notices until the demo.

### Move a card

```
User drags
   → client moves the card on screen NOW, keeping the confirmed state for rollback
   → sends MoveCard { cardId, newStatus, previousCardId, nextCardId, positionVersion }
        │
        ▼
   server: check membership (I3) → check position_version → compute rank
           → write card + event in ONE transaction
        │
        ├── accepted → broadcast CardMoved{seq} to the board group
        │              every client applies it; the sender reconciles its guess
        │
        └── rejected → reply to the sender only
                       client rolls back and shows the true state
```

> *Optimistic update* — the client shows the change before the server confirms it, so the drag feels instant. If the server disagrees, the client undoes it.

**The rollback path is the one that gets skipped and then breaks live.** Test it by dragging a card with the server stopped.

### Edit a card part

```
User edits the title in the modal → sends EditCardText { cardId, title, description, textVersion }
   → server: membership → card not deleted → text_version matches?
        ├── yes → write + event → broadcast CardTextEdited{seq, newVersion}
        └── no  → reject to sender with the current text
                  modal shows the current text + "Someone else changed this"
                  the user's own text stays in the box to copy
```

Due date works the same way with `due_version`. Assignees and subtask ticks have no version check (§7).

### Search

Search filters the cards already in the browser. No server call. It matches the title, description, subtask text and assignee names.

**While a search is active, dragging is turned off.** The move message sends the *neighbour* card IDs (D12). With some cards hidden, the card you see next to the drop point may not be its real neighbour, and the card would land in the wrong place. The Status dropdown still works during a search.

### Presence and cursors

```
On connect  → ZADD presence:{boardId} {now} {connectionId}
              HSET presence:meta:{boardId} {connectionId} {memberId, username, colour}
              broadcast MemberPresent

Every 10s   → client heartbeat → ZADD refreshes the timestamp

Every 5s    → server sweep → removes anyone older than 20s
              → broadcast MemberAway for each

Cursor move → client throttles to 20 per second
              → hub broadcasts straight to the group
              → never written to Postgres, never stored in Redis

Open a card → editing marker in Redis with a 20s expiry, refreshed by the heartbeat
              → broadcast EditingStarted / EditingStopped
```

The sorted set is what makes this self-healing. A browser closed without warning simply stops refreshing its timestamp and gets swept. The 20-second expiry plus at most five seconds until the next sweep keeps an orphaned member's removal within 30 seconds (D14). The sweep atomically rechecks the score before removing it, so a concurrent heartbeat wins.

A joined WebSocket keeps its authorized board, member and role in connection context, so high-frequency cursor messages need no PostgreSQL query. **Writes still recheck membership in PostgreSQL**, because a member can be removed while connected (I3). Rejoining after a reconnect revalidates against PostgreSQL.

### Two servers

```
Browser A ──▶ instance 1 ──┐
                           ├──▶ Redis backplane ──▶ both instances ──▶ both browsers
Browser B ──▶ instance 2 ──┘
```

SignalR groups work across instances through the backplane. **No sticky sessions are needed only when clients use WebSockets exclusively and skip negotiation.** The client must set `WebSockets` as its only transport and `skipNegotiation: true`; the server only permits WebSockets on `/hubs/board`. Supporting fallback transports later would also require session affinity. Worth saying in the README, because the backplane alone does not remove that requirement.

**Dropping a removed member's connection on another instance:** the service broadcasts `MemberRemoved` to the board group. Every instance receives it through the backplane, and each one closes any local connection belonging to that member.

---

## 11. Proving it works

A portfolio project makes claims. These are the tests behind them.

### Convergence test — the headline

20 simulated clients. 200 random operations — moves, text edits, subtask ticks, assignee changes — fired at one board with no delay between them. When everything settles, assert that **every client holds an identical board**: same order, same text, same subtasks, same assignees.

This is what makes I1 real. It is also the one to show in the README, because it is the claim everyone else makes without proof.

### The others

| Test | Proves |
|---|---|
| Two clients move the same card into the same gap | Rank tie-break is stable (I1) |
| Two clients rename the same card from the same version | Second is rejected, sender is told (I4) |
| One client ticks a subtask while another changes the due date | **Both succeed** — separate parts (D17) |
| Two clients add the same assignee at once | Assigned once, no error |
| Edit a card that was just deleted | Rejected with "This card was deleted" |
| A client disconnects, 50 changes happen, it reconnects | Catch-up by `seq` lands on the right state (I5) |
| A client is 600 events behind | Server sends a snapshot instead of a replay (I5) |
| A user sends DeleteBoard / RemoveMember / ResetLink | Rejected (I7) |
| Six wrong codes | The code is dead; a new one is needed |
| A used or expired code | Rejected |
| An already-rotated refresh token is sent again | Its whole chain is revoked |
| An access token expires while the hub is connected | Connection closed; client refreshes, reconnects and catches up |
| Sign-in, verify and refresh requests | Never logged with tokens or codes |
| A member sends a message after the link is reset | Rejected (I3); the admin is unaffected |
| A removed member opens the board link | 404; no new membership (D33) |
| A removed member, connected to the *other* instance, sends a message | Rejected, and their socket was closed (I3, I6) |
| Two API instances in Docker, one client on each | Both see every change (I6) |

Integration tests use **Testcontainers** — real Postgres and real Redis started by the test run, not fakes. Most tests sign in through a **test authentication handler** that sets the same claims a real token would; the account tests run the real code-and-token flow and read the code from Mailpit's API.

### The load test

A script opens **500 concurrent WebSocket connections** across 25 boards and drives realistic traffic.

Record and publish:
- Connections held
- p95 time from one client's action to another client seeing it
- Memory per connection
- Where it falls over, and why

**A measured number in the README beats three paragraphs of description.** k6, or Crank if you want the .NET-native one.

---

## 12. Tech stack

| Layer | Choice | Status | Why |
|---|---|---|---|
| API | **ASP.NET Core, .NET 10 (LTS)** | `[decided]` | .NET 8 loses support 10 Nov 2026 |
| Real-time | **SignalR** | `[decided]` | Groups, reconnect and the backplane are built in |
| Scale-out | **SignalR Redis backplane** | `[decided]` | One line of config; makes I6 possible |
| Database | **PostgreSQL** | `[decided]` | JSONB for event payloads |
| ORM | **EF Core** | `[proposed]` | Ordinary CRUD. The rank and counter updates may be raw SQL |
| Cache / presence | **Redis** | `[decided]` | Sorted sets with timestamps are exactly right for presence |
| Sign-in | **Email code + JWT bearer + rotating refresh token**, built by hand on ASP.NET Core's JWT handler | `[decided]` | Small enough to write and explain yourself (D29, D30) |
| Email (local) | **Mailpit** in Compose | `[decided]` | Catches every email; tests read codes from its API |
| Email (production) | Resend, Amazon SES or Brevo | `[pending]` | Q15, chosen with the host |
| Frontend | **React + TypeScript**, Vite | `[decided]` | |
| Drag and drop | **dnd-kit** | `[proposed]` | Accessible, maintained |
| Server state | **TanStack Query** | `[proposed]` | REST load only. Live state comes from the hub |
| Client state | **React state + pure board projector** | `[decided]` | One board model; replay and rollback can be tested without a browser. See Q11 |
| Styling | **Tailwind CSS v4**, theme tokens from §5 | `[proposed]` | The design exists now, so build it from F0. Tokens live in one place |
| Fonts | Space Grotesk · Archivo Black · Space Mono | `[decided]` | From the Figma design |
| Tests | xUnit · Testcontainers · one Playwright end-to-end run | `[proposed]` | |
| Load test | k6 | `[open]` | Crank is the .NET alternative |
| Local | Docker Compose — Postgres, Redis, **two API instances**, nginx, Mailpit | `[decided]` | Two instances from day one, so I6 is never skipped |
| Observability | **OpenTelemetry** | `[decided]` | Logs, metrics, traces. Where they go depends on the host (§18) |
| CI | GitHub Actions | `[decided]` | |
| Deploy target | AWS on demand **or** Oracle Cloud Always Free + Coolify | `[pending]` | D27, §18, §19 |
| Deploy tool | AWS CDK in C# (if AWS) · Coolify (if Oracle) | `[pending]` | Q6 |

---

## 13. Decisions log

Newest last. Every entry: what, why, date. Change a decision by adding a new entry, not by deleting the old one.

> D1–D14 are kept exactly as written in v1. Section numbers inside them refer to v1 of this file.

**D1 — Portfolio first.** `[decided]` 2026-09-26
The live URL, the README and the demo GIF are deliverables. A feature that cannot be seen in 60 seconds ranks below one that can. *Why:* nobody clones the repo.

**D2 — No accounts.** `[decided]` 2026-09-26 — *revised by D18, replaced by D32*
Join by secret link with a nickname. *Why:* a signup form between a visitor and the demo loses most visitors.

**D3 — Fractional ranks, not integer positions.** `[decided]` 2026-09-26
Cards order by a string rank. Ties break by card id. *Why:* an insert writes one row instead of renumbering a column, and concurrent inserts cannot corrupt the order.

**D4 — Last write wins on card content, with rejection.** `[decided]` 2026-09-26 — *refined by D10, then D17*
Version check on write. A stale write is rejected and the user is told. *Why:* a CRDT is correct and costs weeks. The thing to avoid is a silent overwrite, and a version check avoids it.

**D5 — Two instances in local Compose.** `[decided]` 2026-09-26
*Why:* the multi-instance bug only appears with more than one instance, and by then it is in production.

**D6 — React, not Blazor Server.** `[decided]` 2026-09-26
*Why:* dragging must not need a server round trip, and per-user server memory does not scale for a public demo.

**D7 — The cloud stack is disposable.** `[decided]` 2026-09-26
The whole environment is created and destroyed by one GitHub Actions workflow. It is normally destroyed. It goes up for a demo or an interview and comes down after. *Why:* a running stack costs about $70 a month to serve nobody. Destroying it is only safe if bringing it back is one click, so the teardown and the rebuild are designed together, not bolted on.

**D8 — No state lives only in the cloud.** `[decided]` 2026-09-26
Destroying the stack destroys the database. Boards are demo data, not something to protect. *Why:* if teardown risked losing something real, nobody would ever tear it down. This is a design choice that makes D7 possible.

**D9 — Canonical binary fractional ranks.** `[decided]` 2026-09-26
The rank strings use only `0` and `1` and end in `1`. PostgreSQL uses `C` collation for them. A midpoint always exists between two distinct generated ranks. *Why:* the original claim about arbitrary strings was false for prefix-adjacent values such as `"a"` and `"a0"`.

**D10 — First valid card edit wins.** `[decided]` 2026-09-26
D4's heading said “last write wins,” but its version-check rule rejects a stale second write. The version-check rule is the intended behavior. *Why:* it tells the losing editor about the conflict instead of silently overwriting the accepted edit.

**D11 — WebSockets-only SignalR connections.** `[decided]` 2026-09-26
The browser client uses WebSockets with negotiation skipped, and the hub endpoint disallows fallback transports. *Why:* this makes the no-sticky-sessions claim true when using the Redis backplane. If fallback transports become necessary, add session affinity and revise that claim.

**D12 — The server computes rank from neighbour IDs.** `[decided]` 2026-09-26
The browser immediately changes the visible order, then sends the IDs beside the card's proposed position. The server computes the canonical fractional rank. *Why:* the browser does not need a second rank implementation, and rejection can restore the untouched confirmed state.

**D13 — React state before another state library.** `[decided]` 2026-09-26 — *see Q11*
One board has one confirmed snapshot plus at most one pending move. A pure projector and React state cover this without Zustand. *Why:* the replay and rollback rules remain easy to test directly, and another dependency adds no useful behavior yet.

**D14 — Presence expires in 20 seconds, swept every 5 seconds.** `[decided]` 2026-09-27
The original 30-second expiry and 15-second sweep could leave an orphan visible for about 45 seconds, contradicting Phase 4's under-30-second acceptance condition. Keep the 10-second client heartbeat, but expire after 20 seconds and sweep every five. Normal disconnect removes presence immediately; the Redis sweep handles missed disconnects. One connection owns one board membership, while the UI groups multiple tabs by member ID.

**D15 — Rebuild from Phase 0, by hand, to the Card flow 2 design.** `[decided]` 2026-10-06
v1 reached Phase 4 with AI writing the code. v2 starts again from an empty repo. I write the code; AI reviews it. *Why:* being able to explain and debug every line matters more than having it sooner.

**D16 — Four fixed columns: Ideas, Doing, Review, Done.** `[decided]` 2026-10-06
Every board gets the same four columns. They cannot be added, renamed, reordered or deleted. A card stores its column as a `status` value; there is no columns table. D3's ranks now apply to cards only. *Why:* removes column ranks, column conflicts and the column half of the rank sweep, and the design does not need more.

**D17 — Card edits are checked per part.** `[decided]` 2026-10-06
A card is split into Text, Position and Due date, each with its own version, plus Assignees and Subtasks handled as order-independent operations (§7). Replaces D4/D10's single card version. *Why:* with richer cards, one version would reject writes that never really collided, such as a subtask tick and a due-date change.

**D18 — The board creator signs in with OIDC; people joining stay anonymous.** `[decided]` 2026-10-06 — *OIDC replaced by D25; anonymous joining replaced by D32*
Revises D2. Creating a board requires "Continue with Google". Joining never does. *Why:* the admin role needs an identity that survives cleared cookies and a new laptop, and OIDC gives a real authentication story for interviews. Keeping joins anonymous keeps D2's reason: a recruiter opens the demo link with no wall.

**D19 — Two roles: admin and user.** `[decided]` 2026-10-06
The creator is admin. Admin alone can delete the board, remove a member and reset the link. Checked in the service layer (I7). *Why:* "only the creator can delete it" is the smallest rule that needs real authorization.

**D20 — No category tags in v2.** `[decided]` 2026-10-06
The tag on a card is removed. A header band filled with the column's colour takes its place. Tags are deferred, not dropped. *Why:* user-created tags add a table, a colour picker and another conflict case, and none of it touches the hard problem.

**D21 — Notes merged into Description.** `[decided]` 2026-10-06
*Why:* two free-text fields on one card confuse users and double the text-conflict handling.

**D22 — Search runs in the browser. No voice search.** `[decided]` 2026-10-06
*Why:* the whole board is already loaded, so a server search would add a round trip and a full-text index for no gain. Voice input is not supported in every browser and is outside the project's point.

**D23 — Guessable link format is parked.** `[pending]` 2026-10-06
The `name-xxxx` link has only four random characters. Accepted for now because this is a portfolio project. Must be fixed before the public deployment (§8).

**D24 — New cards go to the top; Status changes go to the bottom.** `[decided]` 2026-10-06
The header button adds to the top of Ideas. A column's button adds to the top of that column. Changing Status in the modal moves the card to the bottom of the new column. *Why:* the add buttons sit at the top of the column, so the new card should appear next to them.


**D25 — User ID + password accounts instead of Google sign-in.** `[decided]` 2026-10-07 — *replaced by D29 the same day*
Replaces the OIDC part of D18. Board creators make an account with a user ID and a password, using ASP.NET Core Identity. People joining by link still need no account. *Why:* no dependency on an outside provider, no registered redirect addresses, and it works the same on every host.

**D26 — A password reset drops all board access.** `[decided]` 2026-10-07 — *void: no passwords after D29*
No email is collected. A reset sets a new password, removes every board membership (admin roles included) and signs out every session. Created boards stay, without an admin. *Why:* without email there is no safe way to prove ownership, so a reset must not hand back anything valuable. Open weakness: Q14.

**D27 — Hosting is undecided.** `[pending]` 2026-10-07
AWS built and destroyed on demand (§18), or an Oracle Cloud Always Free server running Coolify. Decide before the deploy feature (F18 in `stories.md`). *Why parked:* the core does not depend on it.

**D28 — Accounts are built after the core.** `[decided]` 2026-10-07
Until then a **dev sign-in** (Development only) issues a real access token for any email, with no code. Everything after it — membership, roles, the hub — already uses real tokens. F13 swaps in the emailed code. *Why:* if time runs out, the real-time core — the part worth showing — is finished, and accounts are ordinary work.


**D29 — Passwordless sign-in with an emailed code.** `[decided]` 2026-10-07
Replaces D25 and D26. The only sign-in field is the email address. A 6-digit code is emailed; entering it signs the person in, creating the account the first time. The first sign-in asks for a username, changeable later. *Why:* no passwords to store, reset or leak, and the email proves ownership — which closes D26's hole.

**D30 — Access and refresh tokens.** `[decided]` 2026-10-07
A 15-minute JWT access token kept in memory, and a 30-day refresh token in an HttpOnly cookie, rotated on every use. An expired or revoked refresh token means signing in with a new code. *Why:* the industry-standard pattern, and it forces the WebSocket token-expiry case to be handled properly (§8).

**D31 — Guests get tokens too.** `[proposed]` 2026-10-07 — *void: no guests after D32*
Joining by link creates a guest identity and issues the same token pair, without an email. *Why:* one way of checking identity on REST and on the hub, instead of two. When a guest's refresh token ends, they rejoin with a name.


**D32 — Everyone signs in. No guests.** `[decided]` 2026-10-07
Replaces D2 and the anonymous half of D18. Joining a board needs the same emailed-code sign-in as creating one. *Why:* one identity system instead of two, no duplicate members, and a removed member can really be kept out. The cost is a sign-in step for demo visitors; they can use any inbox, such as a public Mailinator address.

**D33 — A removed member cannot rejoin.** `[proposed]` 2026-10-07
With stable accounts, removal is attached to the account, not the link. The join endpoint refuses an account whose membership is marked removed. *Why:* in v1 removing someone did nothing unless the link was also reset. Undoing a removal is deferred.

**D34 — Two health endpoints.** `[decided]` 2026-10-08
- `/health` is the dependency report for people and alarms. It checks Postgres and Redis and returns 503 if either fails.
- `/health/live` checks only that the process is up and does not touch any dependency.
- Compose container health checks use `/health/live`.
- Local nginx fails over passively: it marks an instance down when a request it forwards fails. It does not probe a URL, because active checks need NGINX Plus. The production routing policy is decided together with hosting (§18).

*Why:* routing on the full report would turn a Redis blip into a full outage, even though REST loads still work from Postgres (§6). *Cost:* an instance that loses Redis keeps serving and silently breaks I6 for its clients. A failing `/health` must raise an alarm; it is not a routing signal.

---

## 14. Open questions

**Q1 — How far behind is "too far" for catch-up?** 500 events is a guess. Measure where a snapshot becomes genuinely cheaper.

**Q2 — Does a cursor need smoothing?** Twenty updates a second looks jumpy on a bad connection. Interpolating between points on the client looks better and is more code.

**Q3 — Do idle boards expire?** Only the admin can delete a board (D19). Should a board with no activity for 30 days also be removed automatically? Hosting has limits.

**Q4 — Rank renumbering trigger.** `[decided]` A background sweep runs once a minute and redistributes ranks when one exceeds 50 characters.

**Q5 — Abuse.** The board is open to anyone with the link. Rate limit per connection, a card cap per board, a subtask cap per card. What are the numbers?

**Q6 — CDK or Terraform?** Only if AWS is chosen (D27). CDK is C#, so it is the same language as the app. Terraform appears in more job descriptions.

**Q7 — How long does the stack take to come up from nothing?** RDS is the slow part and can take 10 minutes on its own. Measure it and write the number down.

**Q8 — Does the demo board survive teardown?** A seed step that recreates a demo board on every deploy is more useful than a database snapshot, and far less work. Decide with F18.

**Q9 — A fixed HTTPS address.** The refresh-token cookie is `Secure`, and codes and tokens must never travel in plain text, so the public app needs HTTPS. On AWS the load balancer's address changes on every rebuild, so a fixed domain (Route 53) and a certificate (ACM) are needed. On Oracle + Coolify, Coolify can get a free certificate for a domain you point at the server. Either way, buy a domain before deploying.

**Q10 — Card number prefix.** The design shows `BC-101`. Options: just `#101`, or initials from the board name. Pick in F3.

**Q11 — Client state with many kinds of pending change.** D13 allowed one pending move. Now a user can have a pending move, a pending text edit and a pending subtask tick at the same moment. Does the projector keep a list of pending operations, or one per card part? Decide in F5.

**Q12 — Assignments of a removed member.** Keep them on the card, greyed out? Or remove them? `[open]`

**Q13 — Signed-in user as a guest elsewhere.** `[closed]` by D32 — there are no guests.

**Q14 — Password reset ownership.** `[closed]` by D29 — there are no passwords, and the emailed code proves ownership.

**Q15 — Which email service in production?** Resend, Amazon SES or Brevo. Free tiers differ, and each needs a verified domain (SPF and DKIM records) or codes land in spam. Choose with the host (D27).

**Q16 — Can a person change their email?** Not in scope now. If added, it needs a code sent to the new address.

---

## 15. Phases

The build follows the features in **`stories.md`** (F0–F18), one feature at a time, each with its BE and FE stories finished together. The grouping below is for orientation. **Each feature builds its own screens.**

| Block | Features | Ends with |
|---|---|---|
| **Skeleton** | F0 | Every layer runs locally; a ping crosses two API instances; CI green |
| **Board and cards** | F1–F4 | Create, join, add, edit, delete and drag cards — signed in through the dev sign-in (D28) |
| **Real-time core** | F5–F6 | Two browsers on two instances stay identical; reconnect without refresh (I1, I5, I6) |
| **Rich cards** | F7–F9 | Due dates, assignees, subtasks, and conflicts handled per part (D17) |
| **Live feel** | F10–F12 | Search, presence, cursors, share |
| **Accounts and admin** | F13–F15 | Email-code sign-in, tokens, username, delete board, remove member, reset link |
| **Finish and prove** | F16–F17 | Every screen done, convergence and load tests, README |
| **Deploy** `[pending]` | F18 | Public URL on the chosen host (D27) |

**Rough size:** about 65 hours for Skeleton to Live feel, about 105 hours for everything except Deploy. At 2–3 hours a day, the core is about a month.

---

## 16. The demo — 60 seconds

**0–10s.** Two browser windows side by side, same board. Drag a card from Doing to Review in the left window. It moves in the right one instantly, and its header turns yellow. Cursors move with names attached.

**10–25s.** Open the same card in both windows. The left ticks a subtask while the right changes the due date — **both stick**. Then both rename the card at once — **one is told "Someone else changed this"**. No silent overwrite.

**25–40s.** Kill one API container. The page stays live — the other instance serves it. Bring it back.

**40–60s.** Terminal: the convergence test passing, and the load-test number.

**The GIF at the top of the README is the first ten seconds.** Most visitors never scroll past it.

---

## 17. Demo notes

**Default state is local.** The 60-second demo in §16 runs entirely on Docker Compose. Nothing in it needs a cloud host.

The cloud stack exists for two reasons only:
1. A live link during an active interview process.
2. Proving the deployment story, which is itself portfolio material — most people's projects have no infrastructure code at all.

Record the demo GIF and the full screen recording from the **local** run. Then they exist forever, whether the stack is up or not.

**Seed a demo board whose link is shared directly.** Visitors open the link and sign in with any inbox they can read — a public Mailinator address takes about a minute. Say so next to the link in the README.

---

## 18. Deployment `[pending]`

**Not decided (D27).** Two options:

| | AWS, built and destroyed on demand | Oracle Cloud Always Free + Coolify |
|---|---|---|
| Running cost | ~$2.40 a day while up, near zero while down | Free |
| Live link | Only while the stack is up | Always on |
| What it shows in interviews | Infrastructure as code, ECS, RDS, ElastiCache, CloudWatch | Running your own server, Docker, a self-hosted deploy tool |
| Two API instances | Two Fargate tasks behind an ALB | Two containers behind Coolify's proxy, on one machine |
| Risk | Forgetting to tear down | Oracle can reclaim idle free servers; ARM CPU, so images must build for `arm64` |

The rest of this section is the AWS design, kept for when the choice is made.

### Option A — AWS, built to be destroyed

**The normal state of this stack is "does not exist."** It goes up for a demo and comes down after. That only works if bringing it back is one click, so the teardown and the rebuild are designed together.

### The two workflows

Both are `workflow_dispatch` — a button in the GitHub Actions tab. **Never on push.** A push that spends money is a trap.

```
.github/workflows/stack-up.yml
   build images → push to ECR → cdk deploy → run migrations
   → seed a demo board → print the URL in the job summary

.github/workflows/stack-down.yml
   cdk destroy → confirm nothing is left → print the final cost estimate
```

The `up` workflow ends by printing the URL. The `down` workflow ends by listing what it deleted. Both are screenshots worth keeping.

### What gets created

| Resource | Why | Destroyed with the stack |
|---|---|---|
| VPC, two subnets | Everything lives here | Yes |
| ECS Fargate service, **2 tasks** | The two instances, same as local | Yes |
| ALB | WebSocket-aware routing to both tasks | Yes |
| RDS Postgres `db.t4g.micro` | The truth | **Yes — see below** |
| ElastiCache Redis, smallest node | Backplane and presence | Yes |
| CloudWatch log groups, dashboard, alarms | §18.3 | Yes |
| ECR repository | Holds the images | **No — kept on purpose** |
| Route 53 hosted zone + ACM certificate | A fixed HTTPS address for the `Secure` cookie (Q9) | **No — kept on purpose** |

**ECR, the domain and the certificate survive teardown.** Together they cost under a dollar a month. Keeping them means the next deploy needs no manual setup. Everything else goes.

**RDS is destroyed with no final snapshot, on purpose.** Boards are demo data (D8). A snapshot would be an ongoing cost and a reason to hesitate before tearing down.

### Why a load balancer, and the one trap

An ALB handles WebSockets correctly. It also has a setting that will bite: **idle timeout defaults to 60 seconds.** A WebSocket with no traffic for 60 seconds gets closed by the load balancer, not by your code, and the client reconnects for no reason.

Two fixes, use both:
- Raise the ALB idle timeout to 300 seconds.
- The client already sends a heartbeat every 10 seconds (§10), which keeps the connection busy.

This is worth a paragraph in the README. It is the kind of detail that only comes from having actually deployed something.

**A second trap:** behind any proxy (the ALB, or Coolify's), the app sees plain HTTP and may refuse to send the `Secure` cookie or build `http://` links. Turn on forwarded headers (`UseForwardedHeaders`) so the app knows the original request was HTTPS.

### 18.3 CloudWatch — what actually gets watched

Not "logging is enabled." Specific things, each answering a question you would really ask.

**Custom metrics the app emits:**

| Metric | Answers |
|---|---|
| `ActiveConnections` | How many people are on right now |
| `BroadcastLatencyMs` | Time from message received to broadcast sent |
| `RejectedWrites` (by part: text, position, due date, subtask) | How often each version check rejects a write (I4) |
| `CatchUpRequests` and `SnapshotFallbacks` | Whether reconnect is working or giving up |
| `RankCollisions` | How often two people really do land on the same rank |
| `AuthorizationRejections` | How often I3 and I7 actually fire |

`RankCollisions` is the one to be proud of. It turns a paragraph of theory in the README into an observed number. `RejectedWrites` by part does the same for D17.

**Dashboard** — one screen, and it is a portfolio screenshot in its own right:
- Active connections over time
- p95 broadcast latency
- Requests and 5xx per target
- Database connections and CPU
- Redis connections and evictions

**Alarms** — three, and no more. An alarm nobody acts on is noise.

| Alarm | Fires when | Why it matters |
|---|---|---|
| 5xx rate | More than 1% over 5 minutes | Something is broken |
| p95 broadcast latency | Over 500ms for 5 minutes | Real-time has stopped being real-time |
| **Estimated charges** | **Over $25** | The one that protects you. It fires if you forget to tear down |

The billing alarm matters more than the other two combined. Set it in the first deploy, not the last.

**Log queries** — save two in CloudWatch Logs Insights, ready to run:
- Every rejected write in the last hour, with the card id and the part
- Every reconnect that fell back to a full snapshot

### 18.4 What "it works" means for this phase

The phase is not done when the stack deploys. It is done when this full loop has run:

```
1. Stack does not exist
2. Run stack-up          → note how long it took (Q7)
3. Sign in on the public URL and create a board
4. Open the link from a second device without signing in → the demo works
5. Look at the dashboard → numbers are moving
6. Run stack-down
7. Check the AWS console → nothing left but ECR, the hosted zone and the certificate
8. Check the bill next day → back to near zero
```

**Run the destroy last and leave it destroyed.** A stack left running after testing is the exact failure this design exists to prevent.

---

## 19. Cost

WebSockets are long-lived connections, so this cannot be serverless. While the stack is up, something stays warm.

**While running**, roughly:

| | Per month | Per day |
|---|---|---|
| ALB | ~$18 | ~$0.60 |
| 2 Fargate tasks, small | ~$25 | ~$0.85 |
| RDS `db.t4g.micro` | ~$13 | ~$0.45 |
| ElastiCache, smallest | ~$12 | ~$0.40 |
| CloudWatch, light use | ~$3 | ~$0.10 |
| **Total** | **~$70** | **~$2.40** |

**While destroyed (AWS):** under a dollar a month — ECR images (~$0.10) and the Route 53 hosted zone ($0.50). The ACM certificate is free. **On Oracle Always Free:** nothing, apart from the domain. A domain name is a separate yearly cost, about $10–15. Check current prices before deploying; these are 2026-09 estimates.

That is the whole point of §18. A week of being live during an interview process costs under $20.

**AWS credit: $190, expiring around 7 December 2026.** If AWS is chosen and the core takes about a month, there is still time to use it.

**The billing alarm at $25 is not optional.** The realistic failure here is not a design mistake. It is forgetting to run `stack-down` on a Friday.
