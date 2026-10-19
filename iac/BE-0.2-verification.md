# BE-0.2 local verification — 2026-10-19

Implementation and local acceptance checks passed. Claude's review is still pending.
CI is a separate story, BE-0.5.

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
volume; only the volume name was overridden. Startup and the full stack script
passed again. Docker's image/build cache was retained; this was not a new OS or VM.

The verification stack and its temporary volume were removed. The normal stack
was restored using the original `cardflow-pgdata` volume. No commits were made.
