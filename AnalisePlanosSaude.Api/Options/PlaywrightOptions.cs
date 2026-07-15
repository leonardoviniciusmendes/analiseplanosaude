namespace AnalisePlanosSaude.Api.Options;

public sealed class PlaywrightOptions
{
    public bool Headless { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxContentLength { get; set; } = 2_000_000;
}
