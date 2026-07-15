namespace AnalisePlanosSaude.Api.Entities;

public sealed class AnaliseLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnaliseId { get; set; }
    public string Url { get; set; } = "";
    public AnaliseStatus Status { get; set; } = AnaliseStatus.Pendente;
    public string? ConteudoPagina { get; set; }
    public string? DadosColetadosJson { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessadoEm { get; set; }
    public Analise Analise { get; set; } = null!;
}
