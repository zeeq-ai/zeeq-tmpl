namespace Zeeq.Tmpl;

public record AppSettings
{
    public string LlmApiKey { get; init; } =
        "dotnet user-secrets set AppSettings:LlmApiKey YOUR_API_KEY_HERE";
}
