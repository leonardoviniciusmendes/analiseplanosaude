using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.Coletas;
using Microsoft.AspNetCore.Mvc;

namespace AnalisePlanosSaude.Api.Controllers;

[ApiController]
[Route("api/coletas")]
public sealed class ColetasController(ISimulacaoColetaService coletaService, ISimulacaoAtualizacaoService atualizacaoService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ColetaSimulacaoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ColetaSimulacaoResponse>> Criar(CriarColetaSimulacaoRequest request, CancellationToken cancellationToken)
    {
        return Ok(await coletaService.CriarAsync(request, cancellationToken));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ColetaSimulacaoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ColetaSimulacaoResponse>>> Listar(CancellationToken cancellationToken)
    {
        return Ok(await coletaService.ListarAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ColetaSimulacaoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ColetaSimulacaoResponse>> Obter(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await coletaService.ObterAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/jobs/{tipo}/reagendar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReagendarJob(Guid id, string tipo, CancellationToken cancellationToken)
    {
        await coletaService.ReagendarJobAsync(id, tipo, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/atualizar")]
    [ProducesResponseType(typeof(SimulacaoAtualizacaoJobResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SimulacaoAtualizacaoJobResponse>> AgendarAtualizacao(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await atualizacaoService.AgendarAsync(id, "Manual", cancellationToken));
    }

    [HttpGet("{id:guid}/atualizacoes")]
    [ProducesResponseType(typeof(IReadOnlyList<SimulacaoAtualizacaoJobResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SimulacaoAtualizacaoJobResponse>>> ListarAtualizacoes(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await atualizacaoService.ListarJobsAsync(id, cancellationToken));
    }

    [HttpGet("{id:guid}/versoes")]
    [ProducesResponseType(typeof(IReadOnlyList<SimulacaoColetaVersaoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SimulacaoColetaVersaoResponse>>> ListarVersoes(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await atualizacaoService.ListarVersoesAsync(id, cancellationToken));
    }

    [HttpGet("atualizacoes/jobs")]
    [ProducesResponseType(typeof(IReadOnlyList<SimulacaoAtualizacaoJobResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SimulacaoAtualizacaoJobResponse>>> ListarJobsAtualizacao(CancellationToken cancellationToken)
    {
        return Ok(await atualizacaoService.ListarJobsAsync(null, cancellationToken));
    }
}
