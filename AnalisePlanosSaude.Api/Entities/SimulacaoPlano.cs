namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoPlano
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoColetaId { get; set; }
    public string PlanoIdExterno { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Acomodacao { get; set; }
    public decimal? ValorTotal { get; set; }
    public string? DadosJson { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public SimulacaoColeta SimulacaoColeta { get; set; } = null!;
    public List<SimulacaoValorFaixa> ValoresFaixa { get; set; } = [];
    public List<SimulacaoPrestador> Prestadores { get; set; } = [];
}
