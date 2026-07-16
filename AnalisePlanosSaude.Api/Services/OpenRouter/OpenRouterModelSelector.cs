using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Options;
using AnalisePlanosSaude.Api.Services.Analise;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public sealed class OpenRouterModelSelector(
    AppDbContext db,
    IOptions<OpenRouterOptions> openRouterOptions,
    IOptions<OpenRouterModelosOptions> modelosOptions) : IOpenRouterModelSelector
{
    private readonly OpenRouterOptions _openRouterOptions = openRouterOptions.Value;
    private readonly OpenRouterModelosOptions _modelosOptions = modelosOptions.Value;

    public async Task<string> SelecionarAsync(OpenRouterTipoTarefa tipoTarefa, CancellationToken cancellationToken)
    {
        if (!_modelosOptions.SelecaoAutomatica)
        {
            return ModeloConfiguradoOuErro();
        }

        var query = db.OpenRouterModelos.AsNoTracking().Where(x => x.Ativo);

        query = tipoTarefa switch
        {
            OpenRouterTipoTarefa.NormalizacaoColeta or OpenRouterTipoTarefa.CorrecaoJson => query
                .Where(x => x.RecomendadoNormalizacao || x.SuportaJsonEstruturado)
                .OrderByDescending(x => x.RecomendadoNormalizacao)
                .ThenByDescending(x => x.SuportaJsonEstruturado)
                .ThenByDescending(x => x.CustoBeneficioScore),

            OpenRouterTipoTarefa.MensagemCliente => query
                .Where(x => x.RecomendadoMensagem || x.PrecoInputPorMilhaoTokens != null)
                .OrderByDescending(x => x.RecomendadoMensagem)
                .ThenBy(x => (x.PrecoInputPorMilhaoTokens ?? 0) + (x.PrecoOutputPorMilhaoTokens ?? 0)),

            _ => query
                .Where(x => x.RecomendadoAnalise || x.CustoBeneficioScore > 0)
                .OrderByDescending(x => x.RecomendadoAnalise)
                .ThenByDescending(x => x.CustoBeneficioScore)
        };

        var selecionado = await query.Select(x => x.ModelId).FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(selecionado) ? ModeloConfiguradoOuErro() : selecionado;
    }

    private string ModeloConfiguradoOuErro()
    {
        if (string.IsNullOrWhiteSpace(_openRouterOptions.Model))
        {
            throw new ValidacaoException("FALHA_OPENROUTER", "OpenRouter__Model deve estar configurado ou o catalogo de modelos deve estar sincronizado.");
        }

        return _openRouterOptions.Model;
    }
}
