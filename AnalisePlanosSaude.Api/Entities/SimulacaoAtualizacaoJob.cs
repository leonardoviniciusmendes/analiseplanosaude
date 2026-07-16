namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoAtualizacaoJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoColetaId { get; set; }
    public SimulacaoAtualizacaoJobStatus Status { get; set; } = SimulacaoAtualizacaoJobStatus.Pendente;
    public string Motivo { get; set; } = "";
    public int Tentativas { get; set; }
    public int MaxTentativas { get; set; } = 3;
    public int? VersaoGerada { get; set; }
    public string? DiffJson { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? IniciadoEm { get; set; }
    public DateTime? FinalizadoEm { get; set; }
    public SimulacaoColeta SimulacaoColeta { get; set; } = null!;
}
