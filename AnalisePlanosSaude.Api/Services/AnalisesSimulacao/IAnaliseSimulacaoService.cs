using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.AnalisesSimulacao;

public interface IAnaliseSimulacaoService
{
    Task<AnaliseSimulacaoResponse> CriarAsync(CriarAnaliseSimulacaoRequest request, CancellationToken cancellationToken);
    Task<AnaliseSimulacaoResponse> ObterAsync(Guid id, CancellationToken cancellationToken);
}
