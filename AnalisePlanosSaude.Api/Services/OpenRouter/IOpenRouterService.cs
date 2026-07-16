using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.AnalisesSimulacao;
using AnalisePlanosSaude.Api.Services.Coleta;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public interface IOpenRouterService
{
    Task<IReadOnlyList<PlanoNormalizado>> NormalizarColetaAsync(string cep, IReadOnlyList<int> idades, ColetaLinkResult coleta, CancellationToken cancellationToken);
    Task<ResultadoAnaliseResponse> CompararPlanosAsync(Guid analiseId, string status, string cep, IReadOnlyList<int> idades, IReadOnlyList<string> prioridades, string? observacoes, IReadOnlyList<PlanoNormalizado> planos, int quantidadeLinks, int linksComSucesso, int linksComErro, IReadOnlyList<string> avisos, CancellationToken cancellationToken);
    Task<AnaliseSimulacaoIaTextos> GerarTextosAnaliseSimulacaoAsync(AnaliseSimulacaoDataset dataset, AnaliseSimulacaoResponse resultadoBase, CancellationToken cancellationToken);
}
