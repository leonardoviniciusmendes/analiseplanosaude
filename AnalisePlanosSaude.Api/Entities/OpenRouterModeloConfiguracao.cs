namespace AnalisePlanosSaude.Api.Entities;

public sealed class OpenRouterModeloConfiguracao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OpenRouterTipoTarefa TipoTarefa { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public bool TravadoManual { get; set; }
    public string? Motivo { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
