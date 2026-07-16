namespace AnalisePlanosSaude.Api.Entities;

public sealed class SimulacaoPrestador
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SimulacaoPlanoId { get; set; }
    public string Tipo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Endereco { get; set; }
    public string EspecialidadesJson { get; set; } = "[]";
    public string? TextoEvidencia { get; set; }
    public SimulacaoPlano SimulacaoPlano { get; set; } = null!;
}
