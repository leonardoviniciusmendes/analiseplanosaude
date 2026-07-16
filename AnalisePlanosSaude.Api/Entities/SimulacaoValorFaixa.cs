namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoValorFaixa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoPlanoId { get; set; }
    public string Faixa { get; set; } = "";
    public int? IdadeMin { get; set; }
    public int? IdadeMax { get; set; }
    public decimal Valor { get; set; }
    public SimulacaoPlano SimulacaoPlano { get; set; } = null!;
}
