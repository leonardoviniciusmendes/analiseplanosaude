namespace AnalisePlanosSaude.Api.Options;

public sealed class OpenRouterModelosOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervaloHoras { get; set; } = 24;
    public int HoraExecucao { get; set; } = 2;
    public bool SelecaoAutomatica { get; set; } = true;
    public int MinContextLengthNormalizacao { get; set; } = 32_000;
    public int MinContextLengthAnalise { get; set; } = 16_000;
}
