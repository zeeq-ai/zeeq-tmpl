using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

var username = builder.AddParameter("username", "zeeq", secret: true);
var password = builder.AddParameter("password", "P@ssw0rd", secret: true);
var postgres = builder
    .AddPostgres("postgres", userName: username, password: password)
    .AddDatabase("zeeq");

var csrHook = ResolveCSharpReplHook(); // 👈 Extract the local hook path

var backend = builder
    .AddProject<Projects.server>("app-backend")
    // 👇 Wire up CSharpRepl environment variables to the runtime.
    .WithEnvironment("DOTNET_STARTUP_HOOKS", csrHook)
    .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "CSharpRepl.InjectedHook")
    // 👇 Connect the postgres instance
    .WithReference(postgres);

var frontend = builder
    .AddViteApp(name: "app-frontend", appDirectory: "../src/app")
    .WithPnpm()
    .WithEndpoint(name: "http", port: 7321, scheme: "http")
    .WaitFor(backend);

var kubb = builder
    .AddExecutable(
        name: "kubb-generate-watch",
        command: "pnpm",
        workingDirectory: "../src/app",
        args: ["generate:watch"]
    )
    .WaitFor(frontend);

builder.Build().Run();

string ResolveCSharpReplHook()
{
    var process =
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "csharprepl",
                Arguments = "connect init",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        ) ?? throw new InvalidOperationException("Failed to start csharprepl process.");

    // Full output from the command; need to extract just the hook .dll
    return process
        .StandardOutput.ReadToEnd()
        .Split('\n')
        .Select(line => line.Trim())
        .First(line => line.StartsWith("export DOTNET_STARTUP_HOOKS="))
        .Split('"')[1];
}
