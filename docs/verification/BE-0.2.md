# BE-0.2 local verification â€” 2026-10-19

Implementation and local acceptance checks passed. Claude Code's review on 2026-10-22
is below. CI is a separate story, BE-0.5.

Environment: Windows, .NET SDK 10.0.401, Docker Desktop with Linux containers,
Docker Engine 29.6.1, Docker Compose 5.2.0, PowerShell 7.6.5.

## Checks run

```powershell
docker compose up --build -d --wait --wait-timeout 180
docker compose exec -T nginx nginx -t
pwsh -NoProfile -File scripts/Test-LocalStack.ps1
dotnet build Cardflow.slnx --nologo
dotnet test Cardflow.slnx --no-build --nologo
```

- All six Compose services became healthy.
- Both APIs ran the same image with separate names, `api-1` and `api-2`.
- nginx configuration validation passed.
- Repeated `/health` requests through nginx reached both names and reported healthy
  Postgres and Redis checks.
- API container health checks used `/health/live`; the liveness response had no
  dependency checks.
- Mailpit returned HTTP 200 and its web interface at `http://localhost:8025`.
- With `api-1` stopped, 12 consecutive requests returned HTTP 200 from `api-2`.
  The client did not retry failed requests. nginx performed the failover.
- After restoring `api-1`, both names appeared again.
- Build: 0 warnings, 0 errors. Tests: 10 passed, 0 failed, 0 skipped.
  The unit test project still contains no tests; all 10 are integration cases.
  They include real-container dependency outage/recovery checks and two HTTP
  cases proving default HTTPS redirection and the explicit local HTTP setting.

## Clean-start coverage

A separate local clone received the uncommitted implementation changes, including
new files. No existing `bin` or `obj` files were copied. Build and all 10 tests
passed there.

The normal stack was stopped without deleting its database. The cloned stack
used a separate Compose project, new containers/network, and a new empty Postgres
volume. Startup and the full stack script
passed again. Docker's image/build cache was retained; this was not a new OS or VM.

The verification stack and its temporary volume were removed. The normal stack
was restored. No commits were made.

## Claude Code review — 2026-10-22

A clean clone of `ac8f4d2` built with 0 warnings and passed 10 of 10 tests. The API
image was rebuilt with `--no-cache`, and `scripts/Test-LocalStack.ps1` passed every
check. All five acceptance criteria were met.

Fixed during the review:

- **nginx logged query strings.** `$request` includes the query string, and the hub's
  `access_token` will travel there (`architecture.md` §8). The log now uses
  `$request_method $uri $server_protocol`. The stack script sends a random marker as
  `access_token` and fails if the marker appears in the nginx log.
- **Dockerfile layer order.** Project files are copied and restored first, then `src/`
  is copied, then `publish --no-restore` runs. After a `.cs` change, restore stays cached.
- **Name clash.** The Infrastructure extension class `ServiceCollection` shadowed
  `Microsoft.Extensions.DependencyInjection.ServiceCollection`. It is now
  `InfrastructureServiceCollectionExtensions`, in a file of the same name.
- **WebSocket upgrade through nginx.** `Upgrade` and `Connection` are passed on, ready
  for the hub in BE-0.4.
- **Shared database volume.** The fixed volume name `cardflow-pgdata` is removed.
  Compose now scopes the volume to the project (`cardflow_pgdata`), so every clone
  starts empty. The key is `pgdata`, not `postgres_data`: a v1 volume named
  `cardflow_postgres_data` still exists on this machine with other credentials, and the
  stack script caught the resulting 503s. After the change, all checks passed on a new volume.
- This note moved from `iac/` to `docs/verification/`.
