# Codex prompt — Real-Time Collaborative Board

> Paste §1 into Codex as the opening message, with `architecture.md` attached or already committed at the repo root.
> Then work phase by phase. **Do not paste the whole thing and ask for the finished project.** §5 explains why.

---

## 1. The opening message

```
You are building a real-time collaborative board — a Trello-like board where
several people edit the same board at once and see each other's changes live.

`architecture.md` in the repo root is the brief. Read it fully before writing
any code. It holds the invariants, the data model, the flows and the phases.
It is authoritative. If something I ask for contradicts it, stop and say so
rather than quietly doing one or the other.

## Rules for this repo

1. NOTHING IS DONE UNTIL IT HAS RUN. Do not report a phase complete on the
   strength of code existing. Run the tests, run the app, paste the output.
   If you cannot run something, say exactly that — do not imply it passed.

2. DO NOT BREAK AN INVARIANT IN §3 to make something else easier. If one
   looks wrong, stop and raise it with me.

3. ONE PHASE AT A TIME, from §13. Do not start the next phase until I say so.
   At the end of each phase, give me:
   - what you built, in five lines or fewer
   - the commands to run it myself
   - the test output, pasted
   - anything you were unsure about, and what you chose

4. NO BUSINESS LOGIC IN THE SIGNALR HUB. The hub receives a message, calls a
   service, broadcasts the result. Every rule lives in a service that can be
   tested without a socket. This is the most important structural rule here.

5. WRITE THE TEST WITH THE FEATURE, not after. §9 lists the tests that matter.
   The convergence test is the headline of the whole project.

6. ASK BEFORE ADDING A DEPENDENCY that is not in §10. Name what it does and
   what it replaces.

7. NO SECRETS IN THE REPO. Local config goes in Docker Compose environment
   variables and User Secrets, never in appsettings.json.

8. PLAIN ENGLISH IN EVERYTHING YOU WRITE — commits, comments, the README.
   Short sentences. Keep real technical terms and explain each one the first
   time it appears.

9. COMMIT IN LOGICAL UNITS with real messages. Not 400 commits saying "fix".
   The commit history is part of the portfolio.

10. NOTHING THAT SPENDS MONEY RUNS AUTOMATICALLY. No AWS deploy on push, on
    merge, or on a schedule. Deployment is a manual button only. If you ever
    find yourself adding a cloud step to a push-triggered workflow, stop and
    ask me.

## Stack

.NET 10 · ASP.NET Core · SignalR · PostgreSQL · EF Core · Redis ·
React + TypeScript + Vite · dnd-kit · TanStack Query · Zustand · Tailwind ·
xUnit · Testcontainers · Docker Compose · GitHub Actions ·
AWS CDK in C# · ECS Fargate · RDS · ElastiCache · ALB · CloudWatch

## Start with Phase 0 only

Solution layout, Docker Compose (Postgres, Redis, TWO API instances behind
nginx), CI, a health endpoint, and one Testcontainers test that talks to a
real Postgres.

Two API instances from the first commit — the multi-instance bug is invisible
with one, and it is the bug that only shows up in production.

Before you write anything, tell me:
- the project layout you propose, and why
- how the two instances and nginx will be wired in Compose
- anything in architecture.md that is unclear or that you disagree with
```

---

## 2. Phase kick-off prompts

One per phase. Send each only after you have run the previous phase yourself.

### Phase 1 — Board, no real-time

```
Phase 0 is verified — I ran it. Start Phase 1 from architecture.md §13.

REST only, no SignalR yet. Boards, columns, cards. Join by token with a
nickname and a session cookie.

The important part is the fractional rank in §6. Before you write it:

- explain how you will generate a rank between two existing ranks
- explain what happens when two clients generate the same rank
- explain what happens when ranks grow too long

Then write it, with tests that prove:
- inserting between two cards writes exactly ONE row
- equal ranks break ties by card id, so the order is identical everywhere
- generating a rank at the start, at the end, and in the middle all work
```

### Phase 2 — Real-time

```
Phase 1 verified. Start Phase 2.

SignalR hub, board groups, Redis backplane, the board_events table with seq.

Remember rule 4: the hub holds no logic.

The state change and the event row are written in ONE transaction. Never two.

The exit for this phase is invariant I6, and it has to be proven, not claimed:
a test with two API instances where a client on instance 1 sees a change made
by a client on instance 2. If that test cannot run in CI, tell me now and we
will decide what to do instead.
```

### Phase 3 — Optimistic client and reconnect

```
Phase 2 verified. Start Phase 3.

React board with dnd-kit, optimistic move with rollback, catch-up by seq,
snapshot fallback when too far behind.

Build the ROLLBACK PATH FIRST, not last. It is the part that normally gets
skipped and then fails in a demo. I want to be able to stop the server, drag
a card, and watch it snap back cleanly.

This phase ends with the convergence test from §9: 20 simulated clients, 200
random operations, every client holding an identical card order at the end.
That test is the headline of this project. Do not rush it.
```

### Phase 4 — Presence

```
Phase 3 verified. Start Phase 4.

Members list, colours, live cursors, "editing this card" markers, and the
Redis sorted-set sweep from §8.

Cursors are throttled on the client to about 20 per second and never touch
Postgres.

Prove the sweep: kill a browser tab without warning and show the member
disappearing within 30 seconds.
```

### Phase 5 — Make it look like a product

```
Phase 4 verified. Start Phase 5.

Tailwind pass. Empty states, loading states, a share-link UI, favicon, and an
OG image so the link previews properly when pasted somewhere.

The bar: a stranger should not be able to tell this was built from a tutorial.
Show me two visual directions before you commit to one, then apply the chosen
one consistently.
```

### Phase 6 — Prove it, locally

```
Phase 5 verified. Start Phase 6.

NO CLOUD IN THIS PHASE. Everything runs on local Docker Compose.

Load test with k6 against local Compose: 500 concurrent WebSocket connections
across 25 boards. Report connections held, p95 action-to-visible latency,
memory per connection, and where it breaks. A local number is fine and I will
label it as local in the README.

Wire OpenTelemetry now, exporting to the console locally. Phase 7 swaps the
exporter to CloudWatch, so design it as a config change, not a rewrite.

All environment-specific values — connection strings, Redis address, the
frontend's API URL — come from environment variables. Nothing environment-
specific is baked into appsettings.json.

Then the README. Structure:
1. One-line description
2. The demo GIF — I will record it, leave a placeholder
3. The problem in three sentences: why concurrent edits are hard
4. Architecture diagram
5. How ordering works — fractional ranks and the tie-break
6. How reconnect works — the event log and seq
7. The numbers from the load test, labelled as local
8. How to run it locally — one command, no manual setup
9. What I would do differently

Sections 5 and 6 are what make this more than a Trello clone. Write them
properly. Section 9 is not optional — it is often what an interviewer reads
first.
```

### Phase 7 — Deployment on demand

```
Phase 6 verified. Start Phase 7. Read architecture.md §16 before anything else.

The point of this phase: the stack can be created from nothing and destroyed
completely, by one button each, so it normally does not exist and costs
nothing.

Build, in this order:

1. CDK in C#. VPC, ECS Fargate with 2 tasks, ALB, RDS db.t4g.micro,
   ElastiCache smallest node, ECR, CloudWatch log groups.
   ECR is the only thing that survives destroy.
   RDS is destroyed with NO final snapshot — boards are demo data (D8).

2. THE BILLING ALARM FIRST, before anything else is deployed. Estimated
   charges over $25 sends me an email. This is the alarm that protects me
   from forgetting to tear down.

3. .github/workflows/stack-up.yml   — workflow_dispatch ONLY
   build → push to ECR → cdk deploy → migrate → seed a demo board
   → print the public URL in the job summary

4. .github/workflows/stack-down.yml — workflow_dispatch ONLY
   cdk destroy → verify nothing is left except ECR → print what was deleted

   Neither workflow may ever be triggered by push, merge or schedule.
   AWS credentials via GitHub OIDC, not long-lived access keys.

5. ALB idle timeout set to 300 seconds. The default of 60 closes idle
   WebSockets and causes reconnects that look like a bug in my code.
   Tell me if you find another place this is configured.

6. OpenTelemetry exporter swapped to CloudWatch. The custom metrics in §16.3:
   ActiveConnections, BroadcastLatencyMs, RejectedWrites, CatchUpRequests,
   SnapshotFallbacks, RankCollisions.

7. One CloudWatch dashboard as code, from §16.3. Three alarms, no more:
   5xx rate, p95 broadcast latency, estimated charges.

8. Two saved Logs Insights queries: rejected writes in the last hour, and
   reconnects that fell back to a full snapshot.

Then prove it by running the whole loop in §16.4 — up, demo, dashboard, down,
and confirm nothing is left. Paste each step's output.

Leave the stack DESTROYED at the end. Tell me how long stack-up took, because
that decides how this gets used before an interview.
```

---

## 3. Review prompts — run these on what Codex gives back

These are the questions that find the bugs Codex produces confidently.

```
Walk me through what happens when two clients move the same card into the
same gap at the same millisecond. Trace it from the client through the hub,
the service, the database write, and back to both clients. Show me the exact
code path for each step.
```

```
Show me every place in this code where state is held in process memory.
For each one, tell me what breaks when a second instance starts.
```

```
The state change and the event row must be in one transaction. Show me the
code that guarantees it. If a failure can separate them, say so.
```

```
What happens if the Redis connection drops while 50 people are connected?
Walk me through it. What does the user see?
```

```
Show me the rollback path for an optimistic update. Then show me the test
that exercises it.
```

After Phase 7:

```
List every AWS resource this stack creates. For each one, tell me what it
costs per day while running, and whether `cdk destroy` actually removes it.
Name anything that survives, and why.
```

```
Show me every trigger on every GitHub Actions workflow in this repo. For each
one, tell me whether it can spend money.
```

```
A WebSocket connection sits idle for two minutes. Walk me through what each
piece does — the client, the ALB, the Fargate task. Where would it be closed,
and what stops that happening?
```

---

## 4. Before it goes public

Your own checklist, not Codex's.

| Check | Why |
|---|---|
| Read every file yourself | You will be asked about this code in a room. See §5 |
| Run the convergence test and watch it pass | It is the headline claim |
| Open the board on your phone | Many portfolio visitors are on mobile |
| Paste the link into WhatsApp | The OG image is the first thing anyone sees |
| Kill one container mid-demo | Proves I6 to yourself, not just to CI |
| Squash the junk commits | The history is part of what is reviewed |
| Check the README renders on GitHub | Tables and diagrams break differently there |
| **Run `stack-down` and check the console yourself** | Do not trust the workflow's own report |
| **Check the AWS bill the next day** | The only real proof that teardown worked |
| Confirm the $25 billing alarm emails you | Test it before you need it |

---

## 5. One warning

**You did not write this code, and the interview will not care.**

The projects on your resume are the ones you get questioned about. Fractional ranks, the `seq` catch-up, the backplane — these are exactly the details an interviewer pulls on, because they are the interesting parts.

So the review prompts in §3 are not optional polish. They are how you end up able to explain a codebase you did not type. Run them, read the answers, and push back when one sounds thin.

This is also why the phases exist. Six small reviews you actually read beat one large delivery you skim.
