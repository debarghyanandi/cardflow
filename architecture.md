# Cardflow — Real-Time Collaborative Board

> **Name:** Cardflow. Chosen before the first commit for the repo, namespace and URL.

> **Written by:** Claude, for Debarghya. **Built by:** Codex.
> **Authoritative copy:** this file, in the root of the build repo.
> **Last updated:** 2026-09-26 — first draft, plus the on-demand deployment design (§16). Everything is `[proposed]` until it has run.

---

## 0. How to use this file

**Read this before writing any code. Re-read it from the repo before editing it.**

**Tags:**
- `[decided]` — agreed. Change it only by editing its entry in §11 and saying why.
- `[proposed]` — current best idea, open to argument.
- `[open]` — not decided yet.

**Rules for any agent working here:**

1. **Nothing is done until it has run.** A test passes in the repo, or the output was pasted. A commit message saying "works" is not proof.
2. **Do not break an invariant in §3.** If one seems wrong, stop and raise it.
3. **This is a portfolio project.** The demo and the README are deliverables, not an afterthought. A feature nobody can see in 60 seconds is worth less than one they can.
4. **Plain English in every document.** Short sentences. Keep real technical terms, and explain each one the first time it appears.

---

## 1. What it is

A shared board of columns and cards, like Trello. Several people open the same board at once. One person drags a card, and everyone else sees it move immediately.

Each person sees the others: a coloured dot with a name, a live cursor, and a marker on the card someone is editing.

Anyone with the link can join. There is no signup.

**The pitch, in one sentence:** a collaborative board where the interesting part is not the board — it is what happens when two people touch the same card at the same moment.

---

## 2. The one hard problem

**Two people change the same thing at the same time, and everyone must end up seeing the same board.**

That has a name. **Convergence** — after all messages are delivered, every client shows identical state. Not "roughly the same". Identical.

It shows up in four places.

**Ordering.** Cards in a column have an order. Two people drop a card into the same gap at the same time. If order is an integer position, both writes say "position 3". Either one silently takes the other's place, or the server renumbers every card below. Neither is acceptable.

**Concurrent edits to one card.** Two people rename the same card. Somebody's text has to lose. The real question is whether the loser finds out.

**Reconnects.** A laptop sleeps for ten minutes. It wakes holding a board that is now wrong. It must catch up without downloading everything again, and without showing a half-updated board while it does.

**More than one server.** Two browsers open the same board but land on different server instances. A message sent to one must reach the other. In-memory state does not survive this, and it is the failure that only appears in production.

---

## 3. Invariants — the rules that must always hold

A feature that breaks one of these is a bug, not a trade-off. Each one gets an automated test.

| # | Invariant | Status |
|---|---|---|
| I1 | **Convergence.** After all messages are delivered, every connected client shows the same card order and the same card contents. | `[decided]` |
| I2 | **The server is the only authority.** A client may predict a change and show it at once, but the server's version always wins. A client never writes to another client. | `[decided]` |
| I3 | **Authorization on every message, not just on connect.** Access is checked when a message arrives, not only when the socket opens. A revoked board link stops working on the next message. | `[decided]` |
| I4 | **No lost card.** A card can be moved, renamed or archived. It is never silently dropped because two people touched it at once. | `[decided]` |
| I5 | **Reconnect is exact.** A client that reconnects lands on the same state as one that never disconnected. No page refresh needed. | `[decided]` |
| I6 | **Works across server instances.** Every behaviour above holds with two or more servers behind a load balancer. | `[decided]` |

**I6 is the one that gets skipped.** Everything works on one machine. Run two instances locally from day one so it cannot be skipped.

---

## 4. Scope

### In

- Boards, columns, cards — create, rename, move, archive
- Real-time updates over WebSockets
- Live presence: who is here, their cursor, who is editing which card
- Join by secret link with a nickname. No signup
- Optimistic updates on the client, with rollback when the server disagrees
- Reconnect with catch-up
- Two or more server instances, proven
- A load test with a published number
- **On-demand deployment**: the whole AWS stack built and destroyed by one GitHub Actions run
- CloudWatch dashboards, alarms and log queries

### Cut `[decided]`

| Cut | Why |
|---|---|
| User accounts, email, password reset | A signup wall between a recruiter and the demo destroys the portfolio value |
| Full CRDT or operational transform | Correct, and weeks of work. §6 gives a cheaper design that is honest about its limits |
| Rich text inside cards | A text editor is its own hard problem. Plain text only |
| File attachments | Upload pipeline, storage, scanning. A different project |
| Mobile app | The web page is responsive. That is enough |
| Notifications, email, digests | Invisible in the demo |

### Deferred — only after the core works

- Card comments
- Board templates
- Undo
- Export to JSON or CSV

---

## 5. Architecture `[proposed]`

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
│  boards, columns │                      │ • SignalR backplane  │
│  cards, events   │                      │ • presence + TTL     │
│  THE TRUTH       │                      │ • cursors (never     │
│                  │                      │   written to disk)   │
└──────────────────┘                      └──────────────────────┘
```

### What each part does

**API (.NET).** Two doors into the same application.

- **REST** loads a board when the page opens, and handles anything not time-critical. It returns a snapshot plus a sequence number.
- **SignalR hub** carries live changes both ways. SignalR is Microsoft's real-time library. Cardflow uses WebSockets only; the client skips transport negotiation.

Both doors call the same service layer. **There is no business logic inside the hub.** The hub receives a message, calls a service, broadcasts what the service returns. This is what makes the logic testable without a socket, and it is the single most important structural rule in the project.

**PostgreSQL.** The truth. Board structure, cards, and an append-only list of everything that has happened (§7). If Redis is wiped, nothing is lost.

**Redis.** Three separate jobs. Do not confuse them with each other.

1. **SignalR backplane.** When instance 1 broadcasts to a board, Redis carries it to instance 2. This is what makes I6 possible, and it is one line of configuration.
2. **Presence.** Who is connected to which board, with a timer that removes anyone who stops responding.
3. **Cursor positions.** High volume, worthless after a second. These never touch Postgres.

**Redis is never the source of truth for board content.** If Redis is empty, the board still loads.

**React client.** Vite, TypeScript. `dnd-kit` for dragging. TanStack Query for the REST load. The official SignalR JavaScript client for live updates.

### Why not Blazor Server

Blazor Server would be less code — it already runs over SignalR and updates the page for you. Rejected because every interaction needs a server round trip, so dragging a card feels heavy on a slow connection, and every connected user holds server memory. `[decided]`

This belongs in the README. A rejected option with a reason reads as judgment; a list of chosen technologies does not.

---

## 6. The ordering problem, and how it is solved `[decided]`

**This is the heart of the project. Get this right and the rest is ordinary work.**

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

### The collision, and the tie-break

Two people drop a card into the same gap at the same moment. Both compute a rank between the same neighbours. Both can get `"101"`.

That is fine, and it must be handled on purpose: **when two ranks are equal, sort by card id.** The order is then stable and identical everywhere, which is invariant I1. It may not be the order either person expected, but both see the *same* order, and one small drag fixes it.

**Do not try to prevent the collision.** Preventing it needs a lock on the whole column, and that is a far worse trade.

### Rank exhaustion

Inserting into the same gap over and over makes the strings grow. A background sweep checks once a minute and redistributes the ranks of a column's cards when any active card rank passes 50 characters. It does the same for column ranks on a board. The sweep preserves the existing order and retries if a concurrent write conflicts.

### Card content — stale writes are rejected, and the user is told

Two people rename the same card. The first valid write advances the card's `version`. A write based on the old version is rejected, so it cannot silently replace the accepted text.

- The client sends the version it was looking at.
- If the server's version is higher, the write is **rejected**, not merged.
- The client shows the newer text plus a small note: *"Someone else changed this."*

> *Optimistic concurrency* — the write is allowed to proceed assuming nobody else touched the row, and the assumption is checked at the moment of writing.

This is not the fanciest answer. It is honest, and it never loses data silently, which is I4.

---

## 7. Data model `[proposed]`

Not a schema. The shape.

| Table | Holds |
|---|---|
| `boards` | Id, title, the secret join token, created time |
| `board_members` | An anonymous participant: nickname, colour, session id, board id |
| `columns` | Board id, title, rank |
| `cards` | Column id, title, description, rank, `version`, archived flag |
| `board_events` | **Append-only.** Board id, `seq` (auto-increment per board), type, payload (JSONB), who did it, when |

> *Append-only* — rows are added, never changed or deleted by normal application code.

### `board_events` is what makes reconnect work

Every change writes two things in **one transaction**: the new state, and an event row describing it. If one fails, both fail.

Each event gets a `seq` — a number that only goes up, per board.

- The client holds the last `seq` it has seen.
- Live messages carry their `seq`. Receiving 48 when it expected 47 tells the client it missed one.
- On reconnect it asks for everything after its last `seq`.
- If it is more than 500 events behind, the server sends a fresh snapshot instead. Cheaper than replaying, and simpler than being clever about it.

**Events are pruned** after 7 days or 5,000 rows per board, whichever comes first. They are a catch-up buffer, not an audit log.

### Indexes that will matter

- `cards (column_id, rank)` — every board load sorts by this
- `board_events (board_id, seq)` — every catch-up reads this
- `boards (join_token)` — every join hits this

---

## 8. Main flows `[proposed]`

### Open a board

```
GET /api/boards/{token}
   → snapshot: columns + cards + members + current seq
   → client renders
   → client opens the SignalR connection, sends its seq
   → server sends any events that happened in between
```

**The gap between the snapshot and the socket opening is real.** Something can change in those 200 milliseconds. That is why the client sends its `seq` on connect and the server fills the hole. Skip this and the board is subtly wrong, and nobody notices until the demo.

### Move a card

```
User drags
   → client computes the new rank locally and moves the card on screen NOW
   → sends MoveCard { cardId, newColumnId, newRank, version }
        │
        ▼
   server: check membership (I3) → check version → write card + event in ONE transaction
        │
        ├── accepted → broadcast CardMoved{seq} to the board group
        │              every client applies it; the sender reconciles its guess
        │
        └── rejected → reply to the sender only
                       client rolls back and shows the true state
```

> *Optimistic update* — the client shows the change before the server confirms it, so the drag feels instant. If the server disagrees, the client undoes it.

**The rollback path is the one that gets skipped and then breaks live.** Test it by dragging a card with the server stopped.

### Presence and cursors

```
On connect  → ZADD presence:{boardId} {now} {connectionId}
              HSET presence:meta:{boardId} {connectionId} {nickname, colour}
              broadcast MemberJoined

Every 10s   → client heartbeat → ZADD refreshes the timestamp

Every 15s   → server sweep → ZREMRANGEBYSCORE removes anyone older than 30s
              → broadcast MemberLeft for each

Cursor move → client throttles to 20 per second
              → hub broadcasts straight to the group
              → never written to Postgres, never stored in Redis
```

The sorted set is what makes this self-healing. A browser closed without warning simply stops refreshing its timestamp and gets swept.

### Two servers

```
Browser A ──▶ instance 1 ──┐
                           ├──▶ Redis backplane ──▶ both instances ──▶ both browsers
Browser B ──▶ instance 2 ──┘
```

SignalR groups work across instances through the backplane. **No sticky sessions are needed only when clients use WebSockets exclusively and skip negotiation.** The client must set `WebSockets` as its only transport and `skipNegotiation: true`; the server only permits WebSockets on `/hubs/board`. Supporting fallback transports later would also require session affinity. Worth saying in the README, because the backplane alone does not remove that requirement.

---

## 9. Proving it works

A portfolio project makes claims. These are the tests behind them.

### Convergence test — the headline

20 simulated clients. 200 random operations fired at one board with no delay between them. When everything settles, assert that **every client holds an identical card order**.

This is what makes I1 real. It is also the one to show in the README, because it is the claim everyone else makes without proof.

### The others

| Test | Proves |
|---|---|
| Two clients move the same card into the same gap | Rank tie-break is stable (I1) |
| A client disconnects, 50 changes happen, it reconnects | Catch-up by `seq` lands on the right state (I5) |
| A client is 600 events behind | Server sends a snapshot instead of a replay (I5) |
| Version mismatch on rename | Rejected, not merged; sender is told (I4) |
| Message sent after the board link is rotated | Rejected (I3) |
| Two API instances in Docker, one client on each | Both see every change (I6) |

Integration tests use **Testcontainers** — real Postgres and real Redis started by the test run, not fakes.

### The load test

A script opens **500 concurrent WebSocket connections** across 25 boards and drives realistic traffic.

Record and publish:
- Connections held
- p95 time from one client's action to another client seeing it
- Memory per connection
- Where it falls over, and why

**A measured number in the README beats three paragraphs of description.** k6, or Crank if you want the .NET-native one.

---

## 10. Tech stack

| Layer | Choice | Status | Why |
|---|---|---|---|
| API | **ASP.NET Core, .NET 10 (LTS)** | `[decided]` | .NET 8 loses support 10 Nov 2026 |
| Real-time | **SignalR** | `[decided]` | Groups, reconnect and the backplane are built in |
| Scale-out | **SignalR Redis backplane** | `[decided]` | One line of config; makes I6 possible |
| Database | **PostgreSQL** | `[decided]` | JSONB for event payloads |
| ORM | **EF Core** | `[proposed]` | Ordinary CRUD. No exotic queries here |
| Cache / presence | **Redis** | `[decided]` | Sorted sets with timestamps are exactly right for presence |
| Frontend | **React + TypeScript**, Vite | `[decided]` | |
| Drag and drop | **dnd-kit** | `[proposed]` | Accessible, maintained |
| Server state | **TanStack Query** | `[proposed]` | REST load only. Live state comes from the hub |
| Client state | **Zustand** | `[proposed]` | Small. Redux is too much ceremony for one board |
| Styling | **Tailwind** | `[proposed]` | Portfolio project — it has to look good, fast |
| Identity | Anonymous session cookie + nickname | `[decided]` | No signup wall. §4 |
| Tests | xUnit · Testcontainers · one Playwright end-to-end run | `[proposed]` | |
| Load test | k6 | `[open]` | Crank is the .NET alternative |
| Local | Docker Compose — Postgres, Redis, **two API instances**, nginx | `[decided]` | Two instances from day one, so I6 is never skipped |
| Observability | **OpenTelemetry → CloudWatch** | `[decided]` | Logs, metrics, traces. §16 |
| CI | GitHub Actions | `[decided]` | |
| Deploy | **AWS CDK in C#** | `[proposed]` | Same language as the app. Terraform is the alternative — §12 Q6 |
| Deploy target | AWS — ECS Fargate, RDS, ElastiCache, ALB | `[proposed]` | §16, §17 on cost |
| Deploy trigger | **GitHub Actions, manual only** | `[decided]` | Two buttons: up and down. Never on push — §16 |

---

## 11. Decisions log

Newest last. Every entry: what, why, date. Change a decision by adding a new entry, not by deleting the old one.

**D1 — Portfolio first.** `[decided]` 2026-09-26
The live URL, the README and the demo GIF are deliverables. A feature that cannot be seen in 60 seconds ranks below one that can. *Why:* nobody clones the repo.

**D2 — No accounts.** `[decided]` 2026-09-26
Join by secret link with a nickname. *Why:* a signup form between a visitor and the demo loses most visitors.

**D3 — Fractional ranks, not integer positions.** `[decided]` 2026-09-26
Cards order by a string rank. Ties break by card id. *Why:* an insert writes one row instead of renumbering a column, and concurrent inserts cannot corrupt the order.

**D4 — Last write wins on card content, with rejection.** `[decided]` 2026-09-26
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

The browser client will use WebSockets with negotiation skipped, and the hub endpoint disallows fallback transports. *Why:* this makes the no-sticky-sessions claim true when using the Redis backplane. If fallback transports become necessary, add session affinity and revise that claim.

---

## 12. Open questions

**Q1 — How far behind is "too far" for catch-up?** 500 events is a guess. Measure where a snapshot becomes genuinely cheaper.

**Q2 — Does a cursor need smoothing?** Twenty updates a second looks jumpy on a bad connection. Interpolating between points on the client looks better and is more code.

**Q3 — What happens when the last person leaves a board?** Nothing, or archive after 30 days? Hosting has limits.

**Q4 — Rank renumbering trigger.** `[decided]` A background sweep runs once a minute and redistributes ranks when one exceeds 50 characters. This keeps the slow path out of a normal insert.

**Q5 — Abuse.** The board is open to anyone with the link. Rate limit per connection, and a board size cap. What are the numbers?

**Q6 — CDK or Terraform?** CDK is C#, so it is the same language as the app and one less thing to learn. Terraform appears in more job descriptions. `[open]` — pick one before Phase 7 and say why in this log.

**Q7 — How long does the stack take to come up from nothing?** RDS is the slow part and can take 10 minutes on its own. If the whole thing takes 25 minutes, that changes how it gets used before an interview. Measure it and write the number down.

**Q8 — Does the demo board survive teardown?** A seed step that recreates a demo board on every deploy is more useful than a database snapshot, and far less work. Decide in Phase 7.

---

## 13. Phases

Each phase ends with something checkable in the repo.

**Phase 0 — Skeleton**
Solution layout, Docker Compose with Postgres, Redis, **two API instances** behind nginx, CI, health endpoint.
*Exit:* `docker compose up` works on a clean machine · CI green · a Testcontainers test talks to real Postgres.

**Phase 1 — Board, no real-time**
REST only. Create a board, columns, cards. Fractional ranks. Join by token, nickname, session cookie.
*Exit:* tests prove rank generation including the tie-break · a card inserted between two others writes exactly one row.

**Phase 2 — Real-time**
SignalR hub, board groups, Redis backplane, the event log with `seq`, broadcast on change.
*Exit:* two browsers on **different instances** see each other's changes · a test proves it across instances (I6).

**Phase 3 — Optimistic client and reconnect**
React board, dnd-kit dragging, optimistic move with rollback, catch-up by `seq`, snapshot fallback.
*Exit:* the convergence test passes (§9) · a client that sleeps ten minutes recovers without a refresh (I5).

**Phase 4 — Presence**
Members, colours, live cursors, "editing this card" markers, the Redis sweep.
*Exit:* a closed browser disappears within 30 seconds without sending anything.

**Phase 5 — Make it look like a product**
Tailwind pass, empty states, loading states, a shareable link UI, a favicon, an OG image for link previews.
*Exit:* it does not look like a tutorial.

**Phase 6 — Prove it, locally**
Load test against local Compose, the numbers, OpenTelemetry wired, README with the architecture diagram and the demo GIF.
*Exit:* the load-test number is in the README · the README explains fractional ranks and `seq` catch-up · a stranger can run it with `docker compose up`.

**Phase 7 — Deployment on demand**
Infrastructure as code, the up and down workflows, CloudWatch dashboard, alarms, log queries. §16.
*Exit:* **the stack is created from nothing, the demo works on a public URL, the stack is destroyed, and the AWS bill returns to near zero** — all proven by running it end to end · the destroy is run last and left destroyed.

---

## 14. The demo — 60 seconds

**0–10s.** Two browser windows side by side, same board. Drag a card in the left window. It moves in the right one instantly. Cursors move with names attached.

**10–25s.** Both windows grab the same card at the same moment. Both land on the same result. No duplicate, no vanished card.

**25–40s.** Kill one API container. The page stays live — the other instance serves it. Bring it back.

**40–60s.** Terminal: the convergence test passing, and the load-test number.

**The GIF at the top of the README is the first ten seconds.** Most visitors never scroll past it.

---

## 15. Demo notes

**Default state is local.** The 60-second demo in §14 runs entirely on Docker Compose. Nothing in it needs AWS.

The cloud stack exists for two reasons only:
1. A live link during an active interview process.
2. Proving the deployment story, which is itself portfolio material — most people's projects have no infrastructure code at all.

Record the demo GIF and the full screen recording from the **local** run. Then they exist forever, whether the stack is up or not.

---

## 16. Deployment — built to be destroyed `[proposed]`

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
| CloudWatch log groups, dashboard, alarms | §16.3 | Yes |
| ECR repository | Holds the images | **No — kept on purpose** |

**ECR survives teardown.** It costs about 10 cents a month for a few images, and keeping it means the next deploy skips a full rebuild. Everything else goes.

**RDS is destroyed with no final snapshot, on purpose.** Boards are demo data (D8). A snapshot would be an ongoing cost and a reason to hesitate before tearing down.

### Why a load balancer, and the one trap

An ALB handles WebSockets correctly. It also has a setting that will bite: **idle timeout defaults to 60 seconds.** A WebSocket with no traffic for 60 seconds gets closed by the load balancer, not by your code, and the client reconnects for no reason.

Two fixes, use both:
- Raise the ALB idle timeout to 300 seconds.
- The client already sends a heartbeat every 10 seconds (§8), which keeps the connection busy.

This is worth a paragraph in the README. It is the kind of detail that only comes from having actually deployed something.

### 16.3 CloudWatch — what actually gets watched

Not "logging is enabled." Specific things, each answering a question you would really ask.

**Custom metrics the app emits:**

| Metric | Answers |
|---|---|
| `ActiveConnections` | How many people are on right now |
| `BroadcastLatencyMs` | Time from message received to broadcast sent |
| `RejectedWrites` | How often the version check rejects a write (I4) |
| `CatchUpRequests` and `SnapshotFallbacks` | Whether reconnect is working or giving up |
| `RankCollisions` | How often two people really do land on the same rank |

`RankCollisions` is the one to be proud of. It turns a paragraph of theory in the README into an observed number.

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
- Every rejected write in the last hour, with the card id
- Every reconnect that fell back to a full snapshot

### 16.4 What "it works" means for this phase

The phase is not done when the stack deploys. It is done when this full loop has run:

```
1. Stack does not exist
2. Run stack-up          → note how long it took (Q7)
3. Open the URL from two devices → the demo works
4. Look at the dashboard → numbers are moving
5. Run stack-down
6. Check the AWS console → nothing left but ECR
7. Check the bill next day → back to near zero
```

**Run the destroy last and leave it destroyed.** A stack left running after testing is the exact failure this design exists to prevent.

---

## 17. Cost

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

**While destroyed:** about 10 cents a month for the ECR images. Effectively nothing.

That is the whole point of §16. A week of being live during an interview process costs under $20.

**AWS credit: $190, expiring around 7 December 2026.** Enough for several short runs plus the Phase 7 testing. Spend some of it on Phase 7 before it expires — the deployment story is worth more than the hosting.

**The billing alarm at $25 is not optional.** The realistic failure here is not a design mistake. It is forgetting to run `stack-down` on a Friday.
