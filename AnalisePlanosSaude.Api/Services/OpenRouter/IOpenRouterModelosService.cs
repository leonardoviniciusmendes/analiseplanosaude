using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public interface IOpenRouterModelosService
{
    Task<OpenRouterSincronizacaoModelosResponse> SincronizarAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenRouterModeloResponse>> ListarAsync(bool somenteAtivos, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenRouterModeloRecomendadoResponse>> ListarRecomendadosAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenRouterModeloConfiguracaoResponse>> ListarConfiguracoesAsync(CancellationToken cancellationToken);
    Task<OpenRouterModeloConfiguracaoResponse> ConfigurarModeloAsync(OpenRouterTipoTarefa tipoTarefa, string modelId, string? motivo, CancellationToken cancellationToken);
    Task<OpenRouterModeloConfiguracaoResponse> LimparConfiguracaoAsync(OpenRouterTipoTarefa tipoTarefa, CancellationToken cancellationToken);
    Task<OpenRouterMetricasResumoResponse> ListarMetricasAsync(OpenRouterTipoTarefa? tipoTarefa, int dias, CancellationToken cancellationToken);
    Task<OpenRouterRecalculoScoresResponse> RecalcularScoresAsync(CancellationToken cancellationToken);
    Task RegistrarExecucaoAsync(OpenRouterExecucao execucao, CancellationToken cancellationToken);
}
