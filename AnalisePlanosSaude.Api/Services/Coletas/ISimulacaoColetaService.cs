using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public interface ISimulacaoColetaService
{
    Task<ColetaSimulacaoResponse> CriarAsync(CriarColetaSimulacaoRequest request, CancellationToken cancellationToken);
    Task<ColetaSimulacaoResponse> ObterAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ColetaSimulacaoResponse>> ListarAsync(CancellationToken cancellationToken);
    Task ReagendarJobAsync(Guid id, string tipo, CancellationToken cancellationToken);
}
