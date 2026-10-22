# Cardflow

A collaborative board built with .NET 10. The design and story acceptance criteria
live in [architecture.md](architecture.md) and [stories.md](stories.md).

## Prerequisites

- Docker Desktop running Linux containers, with Docker Compose v2.
- PowerShell to run the local stack check.
- .NET SDK 10.0.401 (or a later patch in the 10.0.4xx band) for building and testing
  outside Docker. `global.json` defines this policy. Docker builds use the pinned SDK.

## Run the local stack

From the repository root:

```powershell
docker compose up --build -d --wait
docker compose ps
```

This starts Postgres, Redis, Mailpit, two API instances, and nginx. Both APIs use
the same image and have explicit names, `api-1` and `api-2`. Only nginx publishes
an API port to the host.

| Service | Address from your computer | Address from another container |
| --- | --- | --- |
| Full health report through nginx | http://localhost:8080/health | http://nginx/health |
| API liveness through nginx | http://localhost:8080/health/live | http://nginx/health/live |
| Postgres | localhost:5433 | postgres:5432 |
| Redis | localhost:6379 | redis:6379 |
| Mailpit web interface | http://localhost:8025 | http://mailpit:8025 |
| Mailpit SMTP | localhost:1025 | mailpit:1025 |

Postgres uses database, username, and password `cardflow`. These are local
development credentials. Published ports bind only to `127.0.0.1`.

If you still have the earlier standalone containers, keep `cardflow-postgres` and
`cardflow-redis` stopped before starting Compose: they use the same host ports.
Compose names the Postgres volume after the project (`cardflow_pgdata`), so
each clone or project name gets its own empty database. The old `cardflow-pgdata`
volume from BE-0.1 is no longer used; remove it with `docker volume rm cardflow-pgdata`
once you no longer need its data. `cardflow_postgres_data`, if present, is left over
from v1 and uses other credentials; it is not used either. Postgres initialization settings only apply to an
empty volume.

```powershell
docker stop cardflow-postgres cardflow-redis
```

Run that command only if those old containers exist.

## Check BE-0.2

```powershell
pwsh -File scripts/Test-LocalStack.ps1
```

The script starts the stack, checks both instance names through nginx, validates
the API container health-check configuration, and opens Mailpit over HTTP. It
then stops `api-1` and requires 12 consecutive successful requests from `api-2`.
It restores `api-1` in a `finally` block and checks that both instances serve again.
Run this against your local development stack; it briefly interrupts `api-1`.
Rebuild with `docker compose up --build -d --wait` first after changing API code.
Recorded local results are in [BE-0.2 verification](docs/verification/BE-0.2.md).

To see the instance names yourself:

```powershell
1..8 | ForEach-Object { (Invoke-RestMethod http://localhost:8080/health).instance }
```

Under D34, `/health` reports Postgres and Redis failures with HTTP 503.
`/health/live` checks only the API process. Compose uses liveness for API health
checks. An unhealthy status alone does not restart a container.

nginx uses round-robin balancing and passive failover: a connection error or
timeout retries the other API. It does not actively poll either health endpoint,
and it does not treat a dependency report's 503 as an API transport failure.
Docker DNS is refreshed so recreated API containers can rejoin without restarting
nginx. `depends_on: service_healthy` gates startup; it does not monitor or stop
dependents after startup.

Local Compose uses plain HTTP and explicitly sets `HttpsRedirection__Enabled=false`.
HTTPS redirection stays enabled by default outside Compose. Public hosting still
requires TLS and trusted forwarded-header configuration before sign-in is deployed
(architecture §18); this local stack does not configure a production proxy policy.

## Build, test, and stop

```powershell
dotnet build Cardflow.slnx
dotnet test Cardflow.slnx --no-build
docker compose down
```

The integration tests start their own temporary Postgres and Redis containers.
The unit test project currently has no tests. `docker compose down` preserves the
database volume. `docker compose down --volumes` deletes the local database.
