---
name: csharprepl
description: Connect to and evaluate C# code in a running .NET application via CSharpRepl.
---

# CSharpRepl

Connects to a running, connector-enabled .NET process and evaluates C# in it — inspect live state (DI services, `DbContext`, caches) or patch behavior without restarting.

## Basic commands

| Command | Purpose |
|---|---|
| `csharprepl connect list` | List connectable processes + PIDs. |
| `csharprepl connect <pid>` | Interactive session attached to a process. |
| `-e <code>` / `--eval <code>` | Eval one snippet, print, exit. |
| `--eval-file <path>` | Eval a `.csx`/`.cs` file, print, exit. |
| `-u <ns>` / `--using <ns>` | Add a using. Local mode only — no effect on `connect` (see below). |

```bash
csharprepl connect list      # find target PID
csharprepl connect 98990     # interactive attach
```

`Get<T>()` inside a connected session = `app.Services.GetRequiredService<T>()`.

## Non-interactive input

- Piped stdin (no flag): whole stdin = 1 submission → `#`-commands fail (`CS1024`).
- `--streamPipedInput`: stdin split per line → `#`-commands and C# can mix freely. Use this for scripting; the interactive UI needs a real TTY and crashes under a piped/synthetic one.
- `--eval-file`: file = 1 submission, not split by line → a `#`-command must be the only line in the file. Run one `--eval-file` invocation per `#`-command against the same PID. Plain multi-line/multi-statement C# with no `#`-commands is fine as a single file.

```bash
# streamPipedInput: multiple lines, NO_COLOR for clean output
printf '%s\n' \
  'Get<Zeeq.Tmpl.HealthHandler>().Handle()' \
  | NO_COLOR=1 csharprepl connect 98990 --streamPipedInput
```

## #replace / #wrap / #patches / #revert

- `#replace <Type>.<Member> with <fn>` — swap implementation for all callers, process-wide. Lambda: `(instance, ...origParams) => ...`.
- `#wrap <Type>.<Member> with <fn>` — like replace but calls through to the original: first param is `orig` (`Func`/`Action` matching instance+params+return), then `instance, ...origParams`. No `ref`/`out`/`in` support (`#replace` supports them).
- `#patches` — list active patches with id. `#revert <id>` — undo one.
- Patches live on the target process (not the CLI session), so they persist across separate `connect`/`--eval-file` invocations against the same PID, until the process restarts/rebuilds.
- Patches don't stack: patching an already-patched member replaces the earlier patch outright.

```bash
# replace, verify via a real HTTP call + aspire logs, then revert
printf '%s\n' \
  '#replace Zeeq.Tmpl.HealthHandler.Handle with (instance) => { Console.WriteLine("REPLACED"); return "Healthy (replaced!)"; }' \
  | NO_COLOR=1 csharprepl connect 98990 --streamPipedInput
curl -s http://localhost:5138/health        # => "Healthy (replaced!)"
aspire logs app-backend -n 10               # => REPLACED
printf '%s\n' '#patches' '#revert 1' | NO_COLOR=1 csharprepl connect 98990 --streamPipedInput
```

```bash
# wrap: orig still runs, original return value passes through unmodified
printf '%s\n' \
  '#wrap Zeeq.Tmpl.HealthHandler.Handle with (Func<Zeeq.Tmpl.HealthHandler, string> orig, Zeeq.Tmpl.HealthHandler instance) => { Console.WriteLine("WRAP called"); var r = orig(instance); Console.WriteLine($"WRAP returned {r}"); return r; }' \
  | NO_COLOR=1 csharprepl connect 98990 --streamPipedInput
curl -s http://localhost:5138/health        # => original value, unmodified
aspire logs app-backend -n 10               # => WRAP called / WRAP returned ...
printf '%s\n' '#patches' '#revert 1' | NO_COLOR=1 csharprepl connect 98990 --streamPipedInput
```

```bash
# same, via --eval-file: one #-command per file/invocation
echo '#replace Zeeq.Tmpl.HealthHandler.Handle with (instance) => "Healthy (replaced!)"' > replace.csx
NO_COLOR=1 csharprepl connect 98990 --eval-file replace.csx
```

## Dropping namespace prefixes

`-u`/`--using` has no effect in `connect` mode. To use short type names against a connected process, submit a real `using` statement as its own line instead — it runs in the target process's script state (like patches, not the CLI session), so it persists across separate invocations against the same PID until the process restarts.

```bash
# once per PID: sets up the using server-side
printf '%s\n' 'using Zeeq.Tmpl;' | NO_COLOR=1 csharprepl connect 98990 --streamPipedInput

# later invocations against the same PID can drop the prefix
echo 'Get<HealthHandler>().Handle()' > call.csx
NO_COLOR=1 csharprepl connect 98990 --eval-file call.csx
```
