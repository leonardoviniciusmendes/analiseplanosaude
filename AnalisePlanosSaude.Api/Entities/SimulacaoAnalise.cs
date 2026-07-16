namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoAnalise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoColetaId { get; set; }
    public string LinkOriginal { get; set; } = "";
    public string HashSimulacao { get; set; } = "";
    public string IdadesJson { get; set; } = "[]";
    public string FaixasUtilizadasJson { get; set; } = "[]";
    public string PrioridadesJson { get; set; } = "[]";
    public string? Observacoes { get; set; }
    public string Status { get; set; } = "Pendente";
    public string? DatasetJson { get; set; }
    public string? ResultadoJson { get; set; }
    public string? ResumoCorretor { get; set; }
    public string? ScriptCorretor { get; set; }
    public string? MensagemWhatsApp { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessadoEm { get; set; }
    public SimulacaoColeta SimulacaoColeta { get; set; } = null!;
}
