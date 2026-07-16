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

    public async Task<string> SelecionarAsync(OpenRouterTipoTarefa tipoTarefa, IReadOnlySet<string>? modelosIgnorados, CancellationToken cancellationToken)
    {
        var ignorados = modelosIgnorados ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuracao = await db.OpenRouterModelosConfiguracoes.AsNoTracking().FirstOrDefaultAsync(x => x.TipoTarefa == tipoTarefa && x.TravadoManual, cancellationToken);
        if (configuracao is not null && !ignorados.Contains(configuracao.ModelId))
        {
            return configuracao.ModelId;
        }

        if (!_modelosOptions.SelecaoAutomatica)
        {
            return ModeloConfiguradoOuErro();
        }

        var candidatos = await BuscarCandidatosAsync(tipoTarefa, ignorados, cancellationToken);
        var selecionado = await SelecionarPorHistoricoAsync(tipoTarefa, candidatos, cancellationToken);
        if (!string.IsNullOrWhiteSpace(selecionado))
        {
            return selecionado;
        }

        var fallback = ModeloConfiguradoOuErro();
        if (!ignorados.Contains(fallback))
        {
            return fallback;
        }

        throw new ValidacaoException("FALHA_OPENROUTER", "Nenhum modelo OpenRouter disponivel para fallback.");
    }

    private async Task<IReadOnlyList<OpenRouterModelo>> BuscarCandidatosAsync(OpenRouterTipoTarefa tipoTarefa, IReadOnlySet<string> ignorados, CancellationToken cancellationToken)
    {
        var ignoradosArray = ignorados.ToArray();
        var query = db.OpenRouterModelos.AsNoTracking().Where(x => x.Ativo && !ignoradosArray.Contains(x.ModelId));

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

        return await query.Take(25).ToArrayAsync(cancellationToken);
    }

    private async Task<string?> SelecionarPorHistoricoAsync(OpenRouterTipoTarefa tipoTarefa, IReadOnlyList<OpenRouterModelo> candidatos, CancellationToken cancellationToken)
    {
        if (candidatos.Count == 0)
        {
            return null;
        }

        var desde = DateTime.UtcNow.AddDays(-Math.Max(1, _modelosOptions.JanelaDiasMetricas));
        var ids = candidatos.Select(x => x.ModelId).ToArray();
        var execucoes = await db.OpenRouterExecucoes.AsNoTracking()
            .Where(x => x.TipoTarefa == tipoTarefa && x.CriadoEm >= desde && ids.Contains(x.ModelId))
            .ToArrayAsync(cancellationToken);

        var metricas = execucoes
            .GroupBy(x => x.ModelId)
            .Select(group =>
            {
                var lista = group.ToArray();
                var total = lista.Length;
                var taxaSucesso = total == 0 ? 0 : lista.Count(x => x.Sucesso) / (decimal)total;
                var tempoMedio = total == 0 ? 0 : lista.Average(x => x.TempoMs);
                var custo = lista.Sum(x => x.CustoEstimado ?? 0);
                return new MetricaModelo(group.Key, total, CalcularScoreOperacional(total, taxaSucesso, tempoMedio, custo));
            })
            .Where(x => x.TotalExecucoes >= Math.Max(1, _modelosOptions.MinExecucoesSelecaoAvancada))
            .ToDictionary(x => x.ModelId, StringComparer.OrdinalIgnoreCase);

        if (metricas.Count == 0)
        {
            return candidatos.First().ModelId;
        }

        return candidatos
            .OrderByDescending(x => metricas.TryGetValue(x.ModelId, out var metrica) ? metrica.ScoreOperacional : 0)
            .ThenByDescending(x => x.CustoBeneficioScore)
            .ThenBy(x => (x.PrecoInputPorMilhaoTokens ?? 0) + (x.PrecoOutputPorMilhaoTokens ?? 0))
            .First()
            .ModelId;
    }

    private static decimal CalcularScoreOperacional(int totalExecucoes, decimal taxaSucesso, double tempoMedioMs, decimal custoTotal)
    {
        var confiabilidade = taxaSucesso * 55m;
        var velocidade = Math.Max(0m, 20m - ((decimal)tempoMedioMs / 2_500m));
        var custoMedio = totalExecucoes == 0 ? 0 : custoTotal / totalExecucoes;
        var custoScore = custoMedio <= 0 ? 15m : Math.Max(0m, 15m - (custoMedio * 10m));
        var evidencia = Math.Min(10m, totalExecucoes);
        return Math.Round(confiabilidade + velocidade + custoScore + evidencia, 4);
    }

    private string ModeloConfiguradoOuErro()
    {
        if (string.IsNullOrWhiteSpace(_openRouterOptions.Model))
        {
            throw new ValidacaoException("FALHA_OPENROUTER", "OpenRouter__Model deve estar configurado ou o catalogo de modelos deve estar sincronizado.");
        }

        return _openRouterOptions.Model;
    }

    private sealed record MetricaModelo(string ModelId, int TotalExecucoes, decimal ScoreOperacional);
}
