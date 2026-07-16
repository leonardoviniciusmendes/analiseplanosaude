namespace AnalisePlanosSaude.Api.Entities;

public sealed class AnaliseComercial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenConsulta { get; set; } = string.Empty;
    public string IdadesJson { get; set; } = "[]";
    public string NecessidadesJson { get; set; } = "[]";
    public string? Cep { get; set; }
    public string? LinkSimulacao { get; set; }
    public string? HashSimulacao { get; set; }
    public string FiltrosJson { get; set; } = "{}";
    public string? PerfilCliente { get; set; }
    public string? PrioridadeVenda { get; set; }
    public string? ObservacoesCorretor { get; set; }
    public string Status { get; set; } = "Pendente";
    public string? DatasetJson { get; set; }
    public string? ResultadoJson { get; set; }
    public string? MelhorPlanoCliente { get; set; }
    public string? MelhorPlanoCorretor { get; set; }
    public string? MensagemCaptacao { get; set; }
    public string? MensagemApresentacao { get; set; }
    public string? MensagemFechamento { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessadoEm { get; set; }
}
