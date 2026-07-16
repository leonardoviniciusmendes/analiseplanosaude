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

public sealed record OpenRouterModeloConfiguracaoResponse(
    OpenRouterTipoTarefa TipoTarefa,
    string? ModelId,
    string? Nome,
    bool TravadoManual,
    string ModoSelecao,
    string? Motivo,
    DateTime? AtualizadoEm);

public sealed record OpenRouterModeloMetricaResponse(
    OpenRouterTipoTarefa TipoTarefa,
    string ModelId,
    string? Nome,
    int TotalExecucoes,
    int TotalSucessos,
    int TotalFalhas,
    decimal TaxaSucesso,
    double TempoMedioMs,
    int TokensInput,
    int TokensOutput,
    decimal CustoTotalEstimado,
    decimal ScoreOperacional,
    DateTime? PrimeiraExecucaoEm,
    DateTime? UltimaExecucaoEm);

public sealed record OpenRouterMetricasResumoResponse(
    OpenRouterTipoTarefa? TipoTarefa,
    int Dias,
    int TotalExecucoes,
    int TotalSucessos,
    int TotalFalhas,
    decimal CustoTotalEstimado,
    IReadOnlyList<OpenRouterModeloMetricaResponse> Modelos);

public sealed record OpenRouterRecalculoScoresResponse(
    int ModelosRecalculados,
    int ExecucoesConsideradas,
    DateTime RecalculadoEm);
