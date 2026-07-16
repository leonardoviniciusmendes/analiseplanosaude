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
    [ProducesResponseType(typeof(AnaliseComercialResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseComercialResponse>> Criar([FromBody] CriarAnaliseComercialRequest request, CancellationToken cancellationToken)
    {
        return Ok(await service.CriarAsync(request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnaliseComercialResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnaliseComercialResponse>> Obter(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await service.ObterAsync(id, cancellationToken));
    }
}
