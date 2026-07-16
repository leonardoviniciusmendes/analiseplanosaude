using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record OpenRouterSincronizacaoModelosResponse(
    int ModelosRecebidos,
    int ModelosCriados,
    int ModelosAtualizados,
    DateTime SincronizadoEm);

public sealed record OpenRouterModeloResponse(
    string ModelId,
    string? Nome,
    string? Provider,
    int? ContextLength,
    decimal? PrecoInputPorMilhaoTokens,
    decimal? PrecoOutputPorMilhaoTokens,
    bool SuportaJsonEstruturado,
    bool SuportaTools,
    bool Ativo,
    bool RecomendadoNormalizacao,
    bool RecomendadoAnalise,
    bool RecomendadoMensagem,
    decimal CustoBeneficioScore,
    int TotalExecucoes,
    int TotalSucessos,
    int TotalFalhas,
    double? TempoMedioMs,
    DateTime UltimaAtualizacao);

public sealed record OpenRouterModeloRecomendadoResponse(
    OpenRouterTipoTarefa TipoTarefa,
    string ModelId,
    string? Nome,
    decimal CustoBeneficioScore,
    string Motivo);
