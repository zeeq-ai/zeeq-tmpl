---
name: aspire-tracing
description: Inspect OpenTelemetry logs, spans, and traces from the running Aspire app via the `aspire` CLI.
---

# Aspire Tracing

Query the Aspire dashboard's telemetry API from the CLI to diagnose issues and trace code — no need to open the dashboard UI.

## Commands

| Command | Purpose |
|---|---|
| `aspire ps` | Find the running AppHost + dashboard URL. |
| `aspire logs <resource> -n <N> --search <text>` | Tail/filter log lines. |
| `aspire logs <resource> -f` | Stream logs live. |
| `aspire otel spans <resource> --search <text>` | List individual spans (attributes, source location). |
| `aspire otel spans <resource> --has-error true` | Only error spans. |
| `aspire otel traces <resource> --search <text>` | List trace summaries (one row/trace + span count). |
| `aspire otel traces <resource> -t <id> --format json` | Full span tree for one trace. |
| `aspire resource app-backend rebuild` | Rebuild + restart after source changes. |

All of `logs`/`otel spans`/`otel traces` support `--search <text>` (server-side full-text/field filter — see `https://aka.ms/aspire/cli-search`) and `--format json|table`.

## `--dashboard-url`

Optional — omitted, the CLI auto-resolves the running AppHost (like `aspire ps`) but prints a `Scanning for running AppHosts...` banner line to stdout first, even under `--nologo`. That banner lands *before* JSON output and breaks naive `| jq`. Two ways to handle it:

```bash
# pass it explicitly (from `aspire ps`) for clean stdout, no banner to strip
aspire otel spans app-backend --search "Health" --dashboard-url http://localhost:15050 --format json | jq .

# or skip the banner line yourself
aspire logs app-backend --search "Health" --format json | tail -n +2 | jq .
```

`aspire logs --format json` prints two banner lines (`Scanning...`, `Getting logs...`); `otel spans`/`otel traces` print one (`Scanning...`) when `--dashboard-url` is omitted.

## JSON shapes

- `logs`: `{"logs":[{"resourceName","content","isError"}]}`. No structured timestamp/level/location — all baked into `content` as `[HH:mm:ss.fff LVL] <message> ( <File>:<Method>@<Line>)`. Source-location suffix only appears for app-emitted (`ILogger`) lines; framework lines have empty `()`.
- `otel spans`: flat array, no wrapper. Each span: `traceId`, `spanId`, (`parentSpanId` for children), `kind`, `name`, `durationMs`, `timestamp`, `attributes`, `dashboardUrl`. App-emitted spans carry source location as structured attributes: `code.file_path`, `code.member`, `code.line_number` — same info as the log suffix, but parseable directly instead of regex'd out of a string.
- `otel traces --format json`: array of trace objects, each with a nested `spans:[...]` (same shape as `otel spans` entries, minus per-span `dashboardUrl`), plus trace-level `dashboardUrl`, `hasError`, `title`. Without `--format json`, `otel traces` is a summary table only — use `otel spans`/`otel traces --format json` to get attributes or source location.

```bash
# jump straight from a trace to every emitting file:line
aspire otel traces app-backend -t <traceId> --format json \
  --dashboard-url http://localhost:15050 | jq '.[0].spans[].attributes | select(."code.file_path")'
```

## Instrumenting code: logging vs. telemetry

Two complementary systems, both wired in `Program.cs` and both auto-tagged with source location — use logs for discrete "what happened" messages, spans for "where did the time go" / request flow.

| | Logging (Serilog) | Telemetry (OpenTelemetry `Activity`/span) |
|---|---|---|
| API | `Serilog.ILogger` + `.Here()` | `ZeeqTelemetry.Trace/SetTags/AddEvent` |
| Unit | Point-in-time message | Duration-bearing span, hierarchical (parent/child) |
| Use for | Human-readable events, warnings, errors | Latency/perf diagnosis, distributed request flow across DB/HTTP calls |
| Source location | `{Here}` string suffix (see JSON shapes above) | `code.*` attributes (see JSON shapes above) |
| Auto-instrumented? | No — call `Log.Here()....` explicitly at each site | Yes for ASP.NET Core/HttpClient/EF Core/Npgsql (`Program.cs` `.WithTracing`); custom spans need `ZeeqTelemetry.Trace` |

They correlate automatically: a log call made while an `Activity` is running (i.e. inside a `using var activity = ZeeqTelemetry.Trace(...)` block) is exported via `WriteTo.OpenTelemetry` and linked to that span/trace in the dashboard — no manual trace-id plumbing needed, since `Activity.Current` is ambient (`AsyncLocal`).

```csharp
// Endpoints/Health.cs
using var activity = ZeeqTelemetry.Trace(tags: [("endpoint", "health")], traceName: "HealthCheck");
Log.Here().Information("Health check requested!");     // linked to the HealthCheck span
await dbContext.Database.ExecuteSqlRawAsync("SELECT 1"); // EF Core instrumentation auto-emits a child span
```

### Telemetry: `ZeeqTelemetry` (`ZeeqTelemetry.cs`)

- `Trace(tags, traceName?)` — starts a new child `Activity` under `Activity.Current`, tagged with `tags` plus auto-captured `code.member`/`code.file_path`/`code.line_number` (`[CallerMemberName]` etc — zero manual bookkeeping). **Must** be `using`'d to dispose/finalize duration. Default name `{File}#{Member}` if `traceName` omitted.
- `SetTags(params tags)` — adds tags to whichever `Activity` is currently running, no new span.
- `AddEvent(tags, eventName?)` — adds a timestamped point-in-time event *inside* the current span. Defaults event name to `{Member}@{File}:{Line}` if omitted.

### Logging: Serilog + `LoggerExtensions.Here()`

Each class declares its own `static readonly` logger, scoped to that type, instead of DI-injecting `ILogger<T>` or calling the static `Serilog.Log` directly:

```csharp
private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<HealthHandler>();
```

- `ForContext<T>()` binds the global `Serilog.Log.Logger` (configured once in `Program.cs`) to a `SourceContext` of the full type name — built once at class load, no DI wiring, no per-request allocation. Scopes/filters output *per class*.
- `.Here()` (`LoggerExtensions.cs`) adds the *per call-site* layer: `[CallerMemberName]`/`[CallerFilePath]`/`[CallerLineNumber]` produce the `{Here}` suffix (see JSON shapes above). Chain it on every call: `Log.Here().Information(...)`.
- Field name `Log` deliberately shadows the `Serilog.Log` static class, so call sites need no `Serilog.` prefix.
