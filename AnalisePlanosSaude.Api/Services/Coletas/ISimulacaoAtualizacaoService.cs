using AnalisePlanosSaude.Api.Models.Responses;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public interface ISimulacaoAtualizacaoService
{
    Task<SimulacaoAtualizacaoJobResponse> AgendarAsync(Guid coletaId, string motivo, CancellationToken cancellationToken);
    Task<int> AgendarVencidasAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SimulacaoAtualizacaoJobResponse>> ListarJobsAsync(Guid? coletaId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SimulacaoColetaVersaoResponse>> ListarVersoesAsync(Guid coletaId, CancellationToken cancellationToken);
}
