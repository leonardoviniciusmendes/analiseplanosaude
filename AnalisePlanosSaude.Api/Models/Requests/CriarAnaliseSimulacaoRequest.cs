namespace AnalisePlanosSaude.Api.Models.Requests;

public sealed record CriarAnaliseSimulacaoRequest(
    string Link,
    IReadOnlyList<int> Idades,
    IReadOnlyList<string>? Prioridades,
    string? Observacoes);
