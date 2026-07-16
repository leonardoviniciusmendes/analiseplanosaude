using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record ColetaSimulacaoResponse(
    Guid Id,
    string UrlOriginal,
    string HashSimulacao,
    string EndpointPrincipal,
    string? EndpointRede,
    SimulacaoColetaStatus Status,
    string? Erro,
    int QuantidadePlanos,
    int QuantidadePrestadores,
    DateTime CriadoEm,
    DateTime AtualizadoEm,
    DateTime? ProcessadoEm,
    IReadOnlyList<ColetaJobResponse> Jobs,
    IReadOnlyList<ColetaPlanoResponse> Planos);

public sealed record ColetaJobResponse(
    Guid Id,
    SimulacaoJobTipo Tipo,
    SimulacaoJobStatus Status,
    int Tentativas,
    string? Erro,
    DateTime CriadoEm,
    DateTime? IniciadoEm,
    DateTime? FinalizadoEm);

public sealed record ColetaPlanoResponse(
    Guid Id,
    string PlanoIdExterno,
    string Nome,
    string? Acomodacao,
    decimal? ValorTotal,
    int QuantidadeHospitais,
    int QuantidadeClinicas,
    int QuantidadeLaboratorios,
    int QuantidadeOutrosPrestadores);
