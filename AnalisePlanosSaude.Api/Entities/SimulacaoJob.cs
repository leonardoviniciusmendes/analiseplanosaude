namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoColetaId { get; set; }
    public SimulacaoJobTipo Tipo { get; set; }
    public SimulacaoJobStatus Status { get; set; } = SimulacaoJobStatus.Pendente;
    public int Tentativas { get; set; }
    public int MaxTentativas { get; set; } = 3;
    public string? PayloadJson { get; set; }
    public string? ResultadoJson { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? IniciadoEm { get; set; }
    public DateTime? FinalizadoEm { get; set; }
    public SimulacaoColeta SimulacaoColeta { get; set; } = null!;
}
