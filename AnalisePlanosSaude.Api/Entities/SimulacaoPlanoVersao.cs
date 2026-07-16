namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoPlanoVersao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoColetaVersaoId { get; set; }
    public string PlanoIdExterno { get; set; } = "";
    public string? Operadora { get; set; }
    public TipoTabelaPlano TipoTabela { get; set; } = TipoTabelaPlano.NaoInformado;
    public string Nome { get; set; } = "";
    public string? Acomodacao { get; set; }
    public decimal? ValorTotal { get; set; }
    public string? DadosJson { get; set; }
    public SimulacaoColetaVersao SimulacaoColetaVersao { get; set; } = null!;
    public List<SimulacaoValorFaixaVersao> ValoresFaixa { get; set; } = [];
    public List<SimulacaoPrestadorVersao> Prestadores { get; set; } = [];
}
