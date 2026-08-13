# .NET 10, C# 14 Web API with Nuxt UI and Vite Frontend

- `src/server`: .NET 10, C# 14; use modern language features (primary constructors, deconstruction, pattern matching, switch expressions, named tuple types, etc.)
- `src/app`: Vue 3, Nuxt UI, Vite, TypeScript, Tailwind CSS

## Skills

- Use the skill `unit-integration-testing` when writing and running .NET C# tests
- Use the skill `csharprepl` to directly manipulate the running C# application, wrap methods, replace methods, and inspect the runtime state of the application

## Tooling

### Nuxt UI Rules

- Prefer Nuxt UI components first before writing new components
- Use Nuxt UI **component slots** first when suitable
- Use Nuxt UI **component props** before writing custom layout or style
- <https://ui.nuxt.com/llms.txt> has dense listing of components available to check
- <https://ui.nuxt.com/llms-full.txt> (download and search)

### Icons, Text, Tailwind

- Use `hugeicons` for Nuxt UI (`i-lucide-*`)

### Aspire CLI

```shell
aspire -h

# Get resources if needed; but usually just use the rebuild command below
aspire ps

# Avoid restarting the full stack; just rebuild the server
aspire resource app-backend rebuild

# Get local dev database connection for psql
aspire describe postgres --apphost ./host --format json | \
  jq -r '.resources[0] | {
  port: (.urls[0].url | split(":")[-1]),
  username: .environment.POSTGRES_USER,
  password: .environment.POSTGRES_PASSWORD,
  db: (.environment.POSTGRES_DB // .environment.POSTGRES_USER)
}'
```

### Playwright MCP

Use the Playwright MCP to run the app in a browser and test the UI.

For UI work, get access first thing **before** you start!
