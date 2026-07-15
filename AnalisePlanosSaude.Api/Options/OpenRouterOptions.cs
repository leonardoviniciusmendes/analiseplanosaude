namespace AnalisePlanosSaude.Api.Options;

public sealed class OpenRouterOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "";
    public string SiteUrl { get; set; } = "https://localhost";
    public string AppName { get; set; } = "Analise Planos Saude";
    public int TimeoutSeconds { get; set; } = 180;
}
