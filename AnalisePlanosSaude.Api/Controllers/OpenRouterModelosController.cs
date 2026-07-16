using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Requests;
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

    [HttpGet("configuracoes")]
    [ProducesResponseType(typeof(IReadOnlyList<OpenRouterModeloConfiguracaoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OpenRouterModeloConfiguracaoResponse>>> Configuracoes(CancellationToken cancellationToken)
    {
        return Ok(await service.ListarConfiguracoesAsync(cancellationToken));
    }

    [HttpGet("metricas")]
    [ProducesResponseType(typeof(OpenRouterMetricasResumoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenRouterMetricasResumoResponse>> Metricas([FromQuery] OpenRouterTipoTarefa? tipoTarefa = null, [FromQuery] int dias = 30, CancellationToken cancellationToken = default)
    {
        return Ok(await service.ListarMetricasAsync(tipoTarefa, dias, cancellationToken));
    }

    [HttpPut("configuracoes/{tipoTarefa}")]
    [ProducesResponseType(typeof(OpenRouterModeloConfiguracaoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenRouterModeloConfiguracaoResponse>> Configurar(OpenRouterTipoTarefa tipoTarefa, [FromBody] ConfigurarOpenRouterModeloRequest request, CancellationToken cancellationToken)
    {
        return Ok(await service.ConfigurarModeloAsync(tipoTarefa, request.ModelId, request.Motivo, cancellationToken));
    }

    [HttpDelete("configuracoes/{tipoTarefa}")]
    [ProducesResponseType(typeof(OpenRouterModeloConfiguracaoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenRouterModeloConfiguracaoResponse>> LimparConfiguracao(OpenRouterTipoTarefa tipoTarefa, CancellationToken cancellationToken)
    {
        return Ok(await service.LimparConfiguracaoAsync(tipoTarefa, cancellationToken));
    }

    [HttpPost("sincronizar")]
    [ProducesResponseType(typeof(OpenRouterSincronizacaoModelosResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenRouterSincronizacaoModelosResponse>> Sincronizar(CancellationToken cancellationToken)
    {
        return Ok(await service.SincronizarAsync(cancellationToken));
    }

    [HttpPost("recalcular-scores")]
    [ProducesResponseType(typeof(OpenRouterRecalculoScoresResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenRouterRecalculoScoresResponse>> RecalcularScores(CancellationToken cancellationToken)
    {
        return Ok(await service.RecalcularScoresAsync(cancellationToken));
    }
}
