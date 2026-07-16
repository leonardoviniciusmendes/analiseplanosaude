using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public interface ISimulacaoHistoricoService
{
    Task<SimulacaoColetaVersao> CriarVersaoAsync(SimulacaoColeta coleta, CancellationToken cancellationToken);
}
