using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.Analise;
using Microsoft.AspNetCore.Mvc;

namespace AnalisePlanosSaude.Api.Controllers;

[ApiController]
[Route("api/analises")]
public sealed class AnalisesController(IAnaliseService analiseService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ResultadoAnaliseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResultadoAnaliseResponse>> Criar(CriarAnaliseRequest request, CancellationToken cancellationToken)
    {
        return Ok(await analiseService.CriarEProcessarAsync(request, cancellationToken));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AnaliseResumoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AnaliseResumoResponse>>> Listar(CancellationToken cancellationToken)
    {
        return Ok(await analiseService.ListarAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnaliseCompletaResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseCompletaResponse>> Obter(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await analiseService.ObterAsync(id, cancellationToken));
    }

    [HttpGet("{id:guid}/resultado")]
    [ProducesResponseType(typeof(ResultadoAnaliseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResultadoAnaliseResponse>> ObterResultado(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await analiseService.ObterResultadoAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/reprocessar")]
    [ProducesResponseType(typeof(ResultadoAnaliseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResultadoAnaliseResponse>> Reprocessar(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await analiseService.ReprocessarAsync(id, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remover(Guid id, CancellationToken cancellationToken)
    {
        await analiseService.RemoverAsync(id, cancellationToken);
        return NoContent();
    }
}
