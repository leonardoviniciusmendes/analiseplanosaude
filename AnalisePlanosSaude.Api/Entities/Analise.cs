namespace AnalisePlanosSaude.Api.Entities;

public sealed class Analise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Cep { get; set; } = "";
    public string IdadesJson { get; set; } = "[]";
    public string PrioridadesJson { get; set; } = "[]";
    public string? Observacoes { get; set; }
    public AnaliseStatus Status { get; set; } = AnaliseStatus.Pendente;
    public string? ResultadoJson { get; set; }
    public string? ResumoCorretor { get; set; }
    public string? ScriptCorretor { get; set; }
    public string? MensagemCliente { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessadoEm { get; set; }
    public List<AnaliseLink> Links { get; set; } = [];
}
