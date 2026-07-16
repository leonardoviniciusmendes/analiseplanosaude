namespace AnalisePlanosSaude.Api.Entities;

public sealed class OpenRouterModelo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ModelId { get; set; } = string.Empty;
    public string? Nome { get; set; }
    public string? Provider { get; set; }
    public int? ContextLength { get; set; }
    public decimal? PrecoInputPorMilhaoTokens { get; set; }
    public decimal? PrecoOutputPorMilhaoTokens { get; set; }
    public bool SuportaJsonEstruturado { get; set; }
    public bool SuportaTools { get; set; }
    public bool Ativo { get; set; } = true;
    public bool RecomendadoNormalizacao { get; set; }
    public bool RecomendadoAnalise { get; set; }
    public bool RecomendadoMensagem { get; set; }
    public decimal CustoBeneficioScore { get; set; }
    public int TotalExecucoes { get; set; }
    public int TotalSucessos { get; set; }
    public int TotalFalhas { get; set; }
    public double? TempoMedioMs { get; set; }
    public DateTime UltimaAtualizacao { get; set; } = DateTime.UtcNow;
}
