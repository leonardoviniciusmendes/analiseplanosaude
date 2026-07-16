namespace AnalisePlanosSaude.Api.Entities;

public sealed class OpenRouterExecucao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OpenRouterTipoTarefa TipoTarefa { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public int? TokensInput { get; set; }
    public int? TokensOutput { get; set; }
    public decimal? CustoEstimado { get; set; }
    public long TempoMs { get; set; }
    public bool Sucesso { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
