namespace AnalisePlanosSaude.Api.Entities;

public sealed class OpenRouterModeloHistorico
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ModelId { get; set; } = string.Empty;
    public decimal? PrecoInputPorMilhaoTokens { get; set; }
    public decimal? PrecoOutputPorMilhaoTokens { get; set; }
    public int? ContextLength { get; set; }
    public bool Ativo { get; set; }
    public decimal CustoBeneficioScore { get; set; }
    public string DadosJson { get; set; } = "{}";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
