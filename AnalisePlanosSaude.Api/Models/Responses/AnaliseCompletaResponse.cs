using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record AnaliseCompletaResponse(
    Guid Id,
    string Cep,
    IReadOnlyList<int> Idades,
    IReadOnlyList<string> Prioridades,
    string? Observacoes,
    AnaliseStatus Status,
    object? Resultado,
    string? ResumoCorretor,
    string? ScriptCorretor,
    string? MensagemCliente,
    string? Erro,
    DateTime CriadoEm,
    DateTime? ProcessadoEm,
    IReadOnlyList<AnaliseLinkResponse> Links);

public sealed record AnaliseLinkResponse(
    Guid Id,
    string Url,
    AnaliseStatus Status,
    string? ConteudoPagina,
    object? DadosColetados,
    string? Erro,
    DateTime CriadoEm,
    DateTime? ProcessadoEm);
