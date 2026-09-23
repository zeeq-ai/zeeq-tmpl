namespace Zeeq.Tmpl;

public record AppSettings
{
    public string LlmApiKey { get; init; } =
        "dotnet user-secrets set AppSettings:LlmApiKey YOUR_API_KEY_HERE";

    /// <summary>
    /// Azure OpenAI endpoint used by the Copilot BYOK provider.
    /// </summary>
    public string LlmBaseUrl { get; init; } = "https://zeeq-open-ai.openai.azure.com";
}
