# An AI enabled, agent-friendly application template

This template is an AI-enabled, agent-friendly application template using .NET 10, C# 15, Vue 3.5, Nuxt UI, and the GitHub Copilot SDK to build an agentic foundation.

This code represents the foundation code built in a 5 part series that is intentionally (hand) written to help dev teams understand how to scaffold a an AI-enabled, agent-friendly codebase (*on an unexpected stack*) by focusing on key, underlying technical decisions and manual wiring *before* building with AI.

Getting the foundations right helps provide the tools and safeguards for coding agents to iterate more efficiently while reducing slop.

Specifically:

- Giving agents access to programmable runtime orchestration ([Aspire.dev](https://aspire.dev/))
- Empowering agents to iterate rapidly with *runtime mutability* (using [CSharpRepl](https://fuqua.io/CSharpRepl/)) to dynamically modify code at runtime while retaining full application state
- Using the GitHub Copilot SDK to build an agentic core with a multi-platform harness, BYOK, any model provider
- [Testcontainers](https://testcontainers.com/) with automatic transactions to streamline and isolate integration tests
- A well-documented, AI-friendly UI framework ([Nuxt UI](https://ui.nuxt.com/))
- Logging and telemetry to give agents insights and visibility into the runtime state of the application

The tech stack is C# and .NET, but the core elements and themes here are applicable to scaffolding on any platform with any programming language (though I think C# particularly good for this!)

The core setup is used at a series C, post-YC startup to ship fast with AI while maintaining high quality standards (in combination with other tools facilitating code review and context management)

[![Video walkthrough of the build out](https://github.com/user-attachments/assets/bdf3940c-6341-4212-af43-66f21b8b4a31)](https://youtu.be/S3NNgr1wMVI)

See the blog posts here:

- <https://chrlschn.dev/blog/2026/08/the-unexpected-ai-stack-csharp-dotnet-part-1/>
- <https://chrlschn.dev/blog/2026/08/the-unexpected-ai-stack-csharp-dotnet-part-2/>
- <https://chrlschn.dev/blog/2026/08/the-unexpected-ai-stack-csharp-dotnet-part-3/>
- <https://chrlschn.dev/blog/2026/08/the-unexpected-ai-stack-csharp-dotnet-part-4/>
- <https://chrlschn.dev/blog/2026/08/the-unexpected-ai-stack-csharp-dotnet-part-5/>

---

**Be sure to see the branches**! `main` is the base; branches and PRs show the progressive layering.

## Tooling

This repo uses [mise](https://mise.jdx.dev/) (`.config/mise.toml`) to manage the .NET SDK and CLI tools (Aspire CLI, CSharpRepl, Csharpier, dotnet-ef). If you have the .NET SDK but not mise, a `.config/dotnet-tools.json` manifest with the same tools is provided as a fallback — run `dotnet tool restore` from the repo root to install them.

> [!IMPORTANT]
> On macOS, the setup of `mise install` for `dotnet` will require a reboot; otherwise, the `dotnet` command yields a `SIGKILL` until reboot and the tools fail to install.

## Minimum Setup

You'll need to set a secret to run the application:

1. Update `AppSettings:LlmBaseUrl` in `src/server/appsettings.json` to set your LLM API endpoint
2. Run `dotnet user-secrets set AppSettings:LlmApiKey "YOUR_API_KEY_HERE"` from `src/server`