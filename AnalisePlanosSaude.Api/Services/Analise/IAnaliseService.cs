using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.Analise;

public interface IAnaliseService
{
    Task<ResultadoAnaliseResponse> CriarEProcessarAsync(CriarAnaliseRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnaliseResumoResponse>> ListarAsync(CancellationToken cancellationToken);
    Task<AnaliseCompletaResponse> ObterAsync(Guid id, CancellationToken cancellationToken);
    Task<ResultadoAnaliseResponse> ObterResultadoAsync(Guid id, CancellationToken cancellationToken);
    Task<ResultadoAnaliseResponse> ReprocessarAsync(Guid id, CancellationToken cancellationToken);
    Task RemoverAsync(Guid id, CancellationToken cancellationToken);
}
