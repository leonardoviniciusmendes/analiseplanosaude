namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record PlanoNormalizado(
    string UrlOrigem,
    string? Operadora,
    string? Plano,
    string? RegistroAns,
    decimal ValorTotal,
    IReadOnlyList<ValorPorIdade> ValoresPorIdade,
    string? TipoContratacao,
    string? Acomodacao,
    string? Abrangencia,
    string? Segmentacao,
    string? Reembolso,
    string? Elegibilidade,
    string? Carencia,
    string? Coparticipacao,
    string? CoparticipacaoTerapias,
    IReadOnlyList<string> DocumentacaoNecessaria,
    IReadOnlyList<string> AreaComercializacao,
    string? Odontologia,
    IReadOnlyList<EstabelecimentoResponse> Hospitais,
    IReadOnlyList<EstabelecimentoResponse> Clinicas,
    IReadOnlyList<EstabelecimentoResponse> Laboratorios,
    IReadOnlyList<EstabelecimentoResponse> CentrosDiagnostico,
    IReadOnlyList<EstabelecimentoResponse> ProntosSocorros,
    IReadOnlyList<string> Observacoes,
    IReadOnlyList<string> Evidencias,
    IReadOnlyList<string> CamposNaoEncontrados);

public sealed record ValorPorIdade(int Idade, decimal Valor);
