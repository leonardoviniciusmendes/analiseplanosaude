namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoColeta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UrlOriginal { get; set; } = "";
    public string HashSimulacao { get; set; } = "";
    public string EndpointPrincipal { get; set; } = "";
    public string? EndpointRede { get; set; }
    public string? JsonPrincipal { get; set; }
    public string? JsonRede { get; set; }
    public SimulacaoColetaStatus Status { get; set; } = SimulacaoColetaStatus.Criada;
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessadoEm { get; set; }
    public List<SimulacaoPlano> Planos { get; set; } = [];
    public List<SimulacaoJob> Jobs { get; set; } = [];
}
