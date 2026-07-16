using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.AnalisesComerciais;

public interface IAnaliseComercialService
{
    Task<AnaliseComercialCriadaResponse> CriarAsync(CriarAnaliseComercialRequest request, CancellationToken cancellationToken);
    Task<AnaliseComercialResponse> ObterAsync(Guid id, CancellationToken cancellationToken);
    Task<AnaliseComercialStatusResponse> ObterStatusAsync(string tokenConsulta, CancellationToken cancellationToken);
    Task<AnaliseComercialResponse> ObterResultadoAsync(string tokenConsulta, CancellationToken cancellationToken);
    Task<bool> ProcessarProximaPendenteAsync(CancellationToken cancellationToken);
}
