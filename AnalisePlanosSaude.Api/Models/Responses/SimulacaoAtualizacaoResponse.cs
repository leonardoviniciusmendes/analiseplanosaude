using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record SimulacaoAtualizacaoJobResponse(
    Guid Id,
    Guid SimulacaoColetaId,
    SimulacaoAtualizacaoJobStatus Status,
    string Motivo,
    int Tentativas,
    int? VersaoGerada,
    string? DiffJson,
    string? Erro,
    DateTime CriadoEm,
    DateTime? IniciadoEm,
    DateTime? FinalizadoEm);

public sealed record SimulacaoColetaVersaoResponse(
    Guid Id,
    Guid SimulacaoColetaId,
    int Versao,
    SimulacaoColetaStatus Status,
    string HashConteudo,
    string DiffJson,
    int QuantidadePlanos,
    int QuantidadePrestadores,
    DateTime CriadoEm,
    DateTime? ProcessadoEm);
