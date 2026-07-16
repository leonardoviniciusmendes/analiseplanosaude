namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoColetaVersao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoColetaId { get; set; }
    public int Versao { get; set; }
    public SimulacaoColetaStatus Status { get; set; }
    public string? JsonPrincipal { get; set; }
    public string? JsonRede { get; set; }
    public string HashConteudo { get; set; } = "";
    public string DiffJson { get; set; } = "{}";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessadoEm { get; set; }
    public SimulacaoColeta SimulacaoColeta { get; set; } = null!;
    public List<SimulacaoPlanoVersao> Planos { get; set; } = [];
}
