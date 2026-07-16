namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoValorFaixaVersao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoPlanoVersaoId { get; set; }
    public string Faixa { get; set; } = "";
    public int? IdadeMin { get; set; }
    public int? IdadeMax { get; set; }
    public decimal Valor { get; set; }
    public SimulacaoPlanoVersao SimulacaoPlanoVersao { get; set; } = null!;
}
