# Orchestra REST API (v1)

Base URL in development: `http://localhost:5080`. OpenAPI document: `/openapi/v1.json` (Development only).

## Authentication
Send an API key in the `X-Api-Key` header. Keys are configured under `Auth:ApiKeys` (see `appsettings.Development.json`
for the development keys). Outside Development the app refuses to start without at least one key of 16+ characters:

```
Auth__ApiKeys__0__Key=<long random string>
Auth__ApiKeys__0__Name=ci
Auth__ApiKeys__0__Role=admin
```

| Role | Can do |
|---|---|
| `reader` | All `GET` endpoints |
| `admin` | Everything, including creating workflows, starting, cancelling and retrying runs |

`/health` and `/` are anonymous. All other endpoints return `401` without a valid key, and `403` for a reader on a write.

## Rate limiting
A token bucket per API key (per IP when anonymous): `RateLimit:TokenLimit` (default 100) refilling at
`RateLimit:TokensPerSecond` (default 50). Over the limit returns `429`.

## Errors
Errors use RFC 7807 problem details. Validation failures are `400` with an `errors` object; state conflicts
(cancelling a finished run, retrying a run that has not failed) are `409`.

## Pagination
List endpoints take `page` (from 1) and `pageSize` (1-100, default 20) and return
`{ "items": [...], "page": 1, "pageSize": 20, "total": 57 }`.

## Endpoints

| Method | Path | Role | Description |
|---|---|---|---|
| POST | `/api/v1/workflows` | admin | Register a workflow. Re-using a name creates the next version. |
| GET | `/api/v1/workflows?name=` | any | List workflow versions (paged) |
| GET | `/api/v1/workflows/{id}` | any | One workflow version |
| POST | `/api/v1/workflows/{id}/runs` | admin | Start a run. Optional `Idempotency-Key` header makes retries safe. `201` when created, `200` when the key was already used. |
| GET | `/api/v1/runs?status=&workflowId=` | any | List runs (paged, newest first) |
| GET | `/api/v1/runs/{id}` | any | Run with all its tasks |
| POST | `/api/v1/runs/{id}/cancel` | admin | Cancel a run that has not finished |
| POST | `/api/v1/runs/{id}/retry` | admin | Retry a failed run: failed and skipped tasks restart, succeeded ones are kept |
| GET | `/api/v1/stats` | any | Counts of workflows, runs by status, tasks by status |

### Example (PowerShell)

```powershell
$h = @{ "X-Api-Key" = "dev-admin-key" }

$wf = Invoke-RestMethod -Method Post http://localhost:5080/api/v1/workflows -Headers $h `
  -ContentType "application/json" -Body (@{
    name  = "nightly-etl"
    tasks = @(
      @{ name = "extract";   handler = "echo";  payload = '{"rows":42}' },
      @{ name = "transform"; handler = "flaky"; dependsOn = @("extract"); maxAttempts = 4; payload = "3" },
      @{ name = "load";      handler = "sleep"; dependsOn = @("transform"); payload = "1500" }
    )
  } | ConvertTo-Json -Depth 5)

$h2 = $h + @{ "Idempotency-Key" = "etl-2026-11-12" }
$run = Invoke-RestMethod -Method Post "http://localhost:5080/api/v1/workflows/$($wf.id)/runs" -Headers $h2

Invoke-RestMethod "http://localhost:5080/api/v1/runs/$($run.id)" -Headers $h
```

### Task fields
| Field | Default | Meaning |
|---|---|---|
| `name` | required | Unique within the workflow |
| `handler` | required | Name of an `ITaskHandler` registered on the workers |
| `dependsOn` | `[]` | Names of tasks that must succeed first |
| `maxAttempts` | 3 | Attempts before the task fails permanently |
| `timeoutSeconds` | 300 | Per-attempt timeout |
| `payload` | null | Opaque string passed to the handler |
