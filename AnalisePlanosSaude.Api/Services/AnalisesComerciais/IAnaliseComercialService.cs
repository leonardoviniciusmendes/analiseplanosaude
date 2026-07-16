using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.AnalisesComerciais;

public interface IAnaliseComercialService
{
    Task<AnaliseComercialResponse> CriarAsync(CriarAnaliseComercialRequest request, CancellationToken cancellationToken);
    Task<AnaliseComercialResponse> ObterAsync(Guid id, CancellationToken cancellationToken);
}
