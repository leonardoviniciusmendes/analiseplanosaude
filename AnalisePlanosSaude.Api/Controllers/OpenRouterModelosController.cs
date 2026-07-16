using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.OpenRouter;
using Microsoft.AspNetCore.Mvc;

namespace AnalisePlanosSaude.Api.Controllers;

[ApiController]
[Route("api/openrouter/modelos")]
public sealed class OpenRouterModelosController(IOpenRouterModelosService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OpenRouterModeloResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OpenRouterModeloResponse>>> Listar([FromQuery] bool somenteAtivos = true, CancellationToken cancellationToken = default)
    {
        return Ok(await service.ListarAsync(somenteAtivos, cancellationToken));
    }

    [HttpGet("recomendados")]
    [ProducesResponseType(typeof(IReadOnlyList<OpenRouterModeloRecomendadoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OpenRouterModeloRecomendadoResponse>>> Recomendados(CancellationToken cancellationToken)
    {
        return Ok(await service.ListarRecomendadosAsync(cancellationToken));
    }

    [HttpPost("sincronizar")]
    [ProducesResponseType(typeof(OpenRouterSincronizacaoModelosResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenRouterSincronizacaoModelosResponse>> Sincronizar(CancellationToken cancellationToken)
    {
        return Ok(await service.SincronizarAsync(cancellationToken));
    }
}
