using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Models.Requests;

public sealed record CriarAnaliseComercialRequest(
    IReadOnlyList<int> Idades,
    IReadOnlyList<string> NecessidadesCliente,
    string? PerfilCliente,
    string? PrioridadeVenda,
    string? Cep,
    string? LinkSimulacao,
    IReadOnlyList<string>? OperadorasPreferidas,
    TipoTabelaPlano? TipoTabela,
    string? ObservacoesCorretor);
