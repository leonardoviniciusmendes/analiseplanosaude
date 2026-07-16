using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public interface IOpenRouterModelosService
{
    Task<OpenRouterSincronizacaoModelosResponse> SincronizarAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenRouterModeloResponse>> ListarAsync(bool somenteAtivos, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenRouterModeloRecomendadoResponse>> ListarRecomendadosAsync(CancellationToken cancellationToken);
    Task RegistrarExecucaoAsync(OpenRouterExecucao execucao, CancellationToken cancellationToken);
}
