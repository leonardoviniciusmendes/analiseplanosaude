using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public interface IOpenRouterModelSelector
{
    Task<string> SelecionarAsync(OpenRouterTipoTarefa tipoTarefa, CancellationToken cancellationToken);
}
