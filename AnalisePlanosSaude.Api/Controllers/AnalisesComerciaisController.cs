using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.AnalisesComerciais;
using Microsoft.AspNetCore.Mvc;

namespace AnalisePlanosSaude.Api.Controllers;

[ApiController]
[Route("api/analises-comerciais")]
public sealed class AnalisesComerciaisController(IAnaliseComercialService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(AnaliseComercialCriadaResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<AnaliseComercialCriadaResponse>> Criar([FromBody] CriarAnaliseComercialRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CriarAsync(request, cancellationToken);
        return Accepted(response.StatusUrl, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnaliseComercialResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseComercialResponse>> Obter(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await service.ObterAsync(id, cancellationToken));
    }

    [HttpGet("{tokenConsulta}/status")]
    [ProducesResponseType(typeof(AnaliseComercialStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseComercialStatusResponse>> ObterStatus(string tokenConsulta, CancellationToken cancellationToken)
    {
        return Ok(await service.ObterStatusAsync(tokenConsulta, cancellationToken));
    }

    [HttpGet("{tokenConsulta}/resultado")]
    [ProducesResponseType(typeof(AnaliseComercialResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseComercialResponse>> ObterResultado(string tokenConsulta, CancellationToken cancellationToken)
    {
        return Ok(await service.ObterResultadoAsync(tokenConsulta, cancellationToken));
    }
}
