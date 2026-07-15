namespace AnalisePlanosSaude.Api.Models.Requests;

public sealed record CriarAnaliseRequest(
    string Cep,
    IReadOnlyList<int> Idades,
    IReadOnlyList<string> Links,
    IReadOnlyList<string>? Prioridades,
    string? Observacoes);
