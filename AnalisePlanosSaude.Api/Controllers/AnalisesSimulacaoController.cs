using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.AnalisesSimulacao;
using Microsoft.AspNetCore.Mvc;

namespace AnalisePlanosSaude.Api.Controllers;

[ApiController]
[Route("api/analises-simulacao")]
public sealed class AnalisesSimulacaoController(IAnaliseSimulacaoService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(AnaliseSimulacaoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseSimulacaoResponse>> Criar([FromBody] CriarAnaliseSimulacaoRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CriarAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnaliseSimulacaoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseSimulacaoResponse>> Obter(Guid id, CancellationToken cancellationToken)
    {
        var response = await service.ObterAsync(id, cancellationToken);
        return Ok(response);
    }
}
