using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record AnaliseResumoResponse(
    Guid Id,
    string Cep,
    IReadOnlyList<int> Idades,
    IReadOnlyList<string> Prioridades,
    string? Observacoes,
    AnaliseStatus Status,
    int QuantidadeLinks,
    string? ResumoCorretor,
    string? MensagemCliente,
    string? Erro,
    DateTime CriadoEm,
    DateTime? ProcessadoEm);
