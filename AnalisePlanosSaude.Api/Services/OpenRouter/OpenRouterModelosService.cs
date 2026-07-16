using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Options;
using AnalisePlanosSaude.Api.Services.Analise;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public sealed class OpenRouterModelosService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<OpenRouterOptions> openRouterOptions,
    IOptions<OpenRouterModelosOptions> modelosOptions) : IOpenRouterModelosService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OpenRouterOptions _openRouterOptions = openRouterOptions.Value;
    private readonly OpenRouterModelosOptions _modelosOptions = modelosOptions.Value;

    public async Task<OpenRouterSincronizacaoModelosResponse> SincronizarAsync(CancellationToken cancellationToken)
    {
        var modelos = await BuscarModelosOpenRouterAsync(cancellationToken);
        var agora = DateTime.UtcNow;
        var ids = modelos.Select(x => x.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existentes = await db.OpenRouterModelos.ToDictionaryAsync(x => x.ModelId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var criados = 0;
        var atualizados = 0;

        foreach (var item in modelos)
        {
            if (!existentes.TryGetValue(item.ModelId, out var modelo))
            {
                modelo = new OpenRouterModelo { ModelId = item.ModelId };
                db.OpenRouterModelos.Add(modelo);
                criados++;
            }
            else
            {
                atualizados++;
            }

            modelo.Nome = item.Nome;
            modelo.Provider = item.Provider;
            modelo.ContextLength = item.ContextLength;
            modelo.PrecoInputPorMilhaoTokens = item.PrecoInputPorMilhaoTokens;
            modelo.PrecoOutputPorMilhaoTokens = item.PrecoOutputPorMilhaoTokens;
            modelo.SuportaJsonEstruturado = item.SuportaJsonEstruturado;
            modelo.SuportaTools = item.SuportaTools;
            modelo.Ativo = true;
            modelo.CustoBeneficioScore = CalcularScore(modelo);
            modelo.UltimaAtualizacao = agora;

            db.OpenRouterModelosHistorico.Add(new OpenRouterModeloHistorico
            {
                ModelId = item.ModelId,
                PrecoInputPorMilhaoTokens = item.PrecoInputPorMilhaoTokens,
                PrecoOutputPorMilhaoTokens = item.PrecoOutputPorMilhaoTokens,
                ContextLength = item.ContextLength,
                Ativo = true,
                CustoBeneficioScore = modelo.CustoBeneficioScore,
                DadosJson = item.DadosJson,
                CriadoEm = agora
            });
        }

        foreach (var modelo in existentes.Values.Where(x => !ids.Contains(x.ModelId)))
        {
            modelo.Ativo = false;
            modelo.UltimaAtualizacao = agora;
        }

        await db.SaveChangesAsync(cancellationToken);
        await RecalcularRecomendacoesAsync(cancellationToken);

        return new OpenRouterSincronizacaoModelosResponse(modelos.Count, criados, atualizados, agora);
    }

    public async Task<IReadOnlyList<OpenRouterModeloResponse>> ListarAsync(bool somenteAtivos, CancellationToken cancellationToken)
    {
        var query = db.OpenRouterModelos.AsNoTracking();
        if (somenteAtivos)
        {
            query = query.Where(x => x.Ativo);
        }

        return await query
            .OrderByDescending(x => x.CustoBeneficioScore)
            .ThenBy(x => x.PrecoInputPorMilhaoTokens ?? decimal.MaxValue)
            .Select(x => ToResponse(x))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OpenRouterModeloRecomendadoResponse>> ListarRecomendadosAsync(CancellationToken cancellationToken)
    {
        var modelos = await db.OpenRouterModelos.AsNoTracking().Where(x => x.Ativo).ToArrayAsync(cancellationToken);
        return
        [
            ToRecomendado(OpenRouterTipoTarefa.NormalizacaoColeta, EscolherNormalizacao(modelos), "Melhor equilibrio para extracao estruturada e JSON."),
            ToRecomendado(OpenRouterTipoTarefa.AnalisePlanos, EscolherAnalise(modelos), "Melhor equilibrio para raciocinio comercial e contexto."),
            ToRecomendado(OpenRouterTipoTarefa.AnaliseSimulacao, EscolherAnalise(modelos), "Melhor equilibrio para comparar planos salvos."),
            ToRecomendado(OpenRouterTipoTarefa.AnaliseComercial, EscolherAnalise(modelos), "Melhor equilibrio para analise comercial e roteiro de venda."),
            ToRecomendado(OpenRouterTipoTarefa.MensagemCliente, EscolherMensagem(modelos), "Modelo de menor custo suficiente para texto curto."),
            ToRecomendado(OpenRouterTipoTarefa.CorrecaoJson, EscolherNormalizacao(modelos), "Modelo com boa aderencia a JSON para correcao simples.")
        ];
    }

    public async Task<IReadOnlyList<OpenRouterModeloConfiguracaoResponse>> ListarConfiguracoesAsync(CancellationToken cancellationToken)
    {
        var configuracoes = await db.OpenRouterModelosConfiguracoes.AsNoTracking().ToArrayAsync(cancellationToken);
        var modelos = await db.OpenRouterModelos.AsNoTracking().ToDictionaryAsync(x => x.ModelId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return Enum.GetValues<OpenRouterTipoTarefa>()
            .Select(tipo =>
            {
                var config = configuracoes.FirstOrDefault(x => x.TipoTarefa == tipo);
                modelos.TryGetValue(config?.ModelId ?? "", out var modelo);
                return ToConfiguracaoResponse(tipo, config, modelo);
            })
            .ToArray();
    }

    public async Task<OpenRouterModeloConfiguracaoResponse> ConfigurarModeloAsync(OpenRouterTipoTarefa tipoTarefa, string modelId, string? motivo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ValidacaoException("REQUISICAO_INVALIDA", "Informe o ModelId do OpenRouter.");
        }

        modelId = modelId.Trim();
        var agora = DateTime.UtcNow;
        var config = await db.OpenRouterModelosConfiguracoes.FirstOrDefaultAsync(x => x.TipoTarefa == tipoTarefa, cancellationToken);
        if (config is null)
        {
            config = new OpenRouterModeloConfiguracao
            {
                TipoTarefa = tipoTarefa,
                CriadoEm = agora
            };
            db.OpenRouterModelosConfiguracoes.Add(config);
        }

        config.ModelId = modelId;
        config.TravadoManual = true;
        config.Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        config.AtualizadoEm = agora;

        await db.SaveChangesAsync(cancellationToken);

        var modelo = await db.OpenRouterModelos.AsNoTracking().FirstOrDefaultAsync(x => x.ModelId == modelId, cancellationToken);
        return ToConfiguracaoResponse(tipoTarefa, config, modelo);
    }

    public async Task<OpenRouterModeloConfiguracaoResponse> LimparConfiguracaoAsync(OpenRouterTipoTarefa tipoTarefa, CancellationToken cancellationToken)
    {
        var config = await db.OpenRouterModelosConfiguracoes.FirstOrDefaultAsync(x => x.TipoTarefa == tipoTarefa, cancellationToken);
        if (config is not null)
        {
            db.OpenRouterModelosConfiguracoes.Remove(config);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToConfiguracaoResponse(tipoTarefa, null, null);
    }

    public async Task<OpenRouterMetricasResumoResponse> ListarMetricasAsync(OpenRouterTipoTarefa? tipoTarefa, int dias, CancellationToken cancellationToken)
    {
        dias = Math.Clamp(dias, 1, 365);
        var desde = DateTime.UtcNow.AddDays(-dias);
        var query = db.OpenRouterExecucoes.AsNoTracking().Where(x => x.CriadoEm >= desde);
        if (tipoTarefa is not null)
        {
            query = query.Where(x => x.TipoTarefa == tipoTarefa);
        }

        var execucoes = await query.ToArrayAsync(cancellationToken);
        var modelos = await db.OpenRouterModelos.AsNoTracking().ToDictionaryAsync(x => x.ModelId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var metricas = execucoes
            .GroupBy(x => new { x.TipoTarefa, x.ModelId })
            .Select(group =>
            {
                modelos.TryGetValue(group.Key.ModelId, out var modelo);
                return CriarMetrica(group.Key.TipoTarefa, group.Key.ModelId, modelo?.Nome, group);
            })
            .OrderByDescending(x => x.ScoreOperacional)
            .ThenBy(x => x.CustoTotalEstimado)
            .ToArray();

        return new OpenRouterMetricasResumoResponse(
            tipoTarefa,
            dias,
            execucoes.Length,
            execucoes.Count(x => x.Sucesso),
            execucoes.Count(x => !x.Sucesso),
            execucoes.Sum(x => x.CustoEstimado ?? 0),
            metricas);
    }

    public async Task<OpenRouterRecalculoScoresResponse> RecalcularScoresAsync(CancellationToken cancellationToken)
    {
        var modelos = await db.OpenRouterModelos.ToArrayAsync(cancellationToken);
        var execucoes = await db.OpenRouterExecucoes.AsNoTracking().ToArrayAsync(cancellationToken);
        var grupos = execucoes.GroupBy(x => x.ModelId).ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var modelo in modelos)
        {
            if (grupos.TryGetValue(modelo.ModelId, out var grupo))
            {
                var lista = grupo.ToArray();
                modelo.TotalExecucoes = lista.Length;
                modelo.TotalSucessos = lista.Count(x => x.Sucesso);
                modelo.TotalFalhas = lista.Count(x => !x.Sucesso);
                modelo.TempoMedioMs = lista.Length == 0 ? null : Math.Round(lista.Average(x => x.TempoMs), 2);
            }
            else
            {
                modelo.TotalExecucoes = 0;
                modelo.TotalSucessos = 0;
                modelo.TotalFalhas = 0;
                modelo.TempoMedioMs = null;
            }

            modelo.CustoBeneficioScore = CalcularScore(modelo);
        }

        await db.SaveChangesAsync(cancellationToken);
        await RecalcularRecomendacoesAsync(cancellationToken);

        return new OpenRouterRecalculoScoresResponse(modelos.Length, execucoes.Length, DateTime.UtcNow);
    }

    public async Task RegistrarExecucaoAsync(OpenRouterExecucao execucao, CancellationToken cancellationToken)
    {
        db.OpenRouterExecucoes.Add(execucao);

        var modelo = await db.OpenRouterModelos.FirstOrDefaultAsync(x => x.ModelId == execucao.ModelId, cancellationToken);
        if (modelo is not null)
        {
            execucao.CustoEstimado = CalcularCusto(execucao, modelo);
            modelo.TotalExecucoes++;
            if (execucao.Sucesso)
            {
                modelo.TotalSucessos++;
            }
            else
            {
                modelo.TotalFalhas++;
            }

            modelo.TempoMedioMs = modelo.TempoMedioMs is null
                ? execucao.TempoMs
                : Math.Round((modelo.TempoMedioMs.Value * (modelo.TotalExecucoes - 1) + execucao.TempoMs) / modelo.TotalExecucoes, 2);
            modelo.CustoBeneficioScore = CalcularScore(modelo);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ModeloOpenRouterDto>> BuscarModelosOpenRouterAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("OpenRouter");
        client.BaseAddress = new Uri(_openRouterOptions.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Remove("HTTP-Referer");
        client.DefaultRequestHeaders.Remove("X-Title");

        if (!string.IsNullOrWhiteSpace(_openRouterOptions.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openRouterOptions.ApiKey);
        }

        client.DefaultRequestHeaders.Add("HTTP-Referer", _openRouterOptions.SiteUrl);
        client.DefaultRequestHeaders.Add("X-Title", _openRouterOptions.AppName);

        using var response = await client.GetAsync("models", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(content);
        var data = doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array
            ? dataElement
            : doc.RootElement;

        var modelos = new List<ModeloOpenRouterDto>();
        foreach (var item in data.EnumerateArray())
        {
            var modelId = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(modelId))
            {
                continue;
            }

            var pricing = item.TryGetProperty("pricing", out var pricingElement) ? pricingElement : default;
            var supportedParameters = item.TryGetProperty("supported_parameters", out var parametersElement) && parametersElement.ValueKind == JsonValueKind.Array
                ? parametersElement.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : [];

            modelos.Add(new ModeloOpenRouterDto(
                modelId,
                GetString(item, "name"),
                ExtrairProvider(modelId),
                GetInt(item, "context_length"),
                ToPrecoPorMilhao(GetDecimal(pricing, "prompt")),
                ToPrecoPorMilhao(GetDecimal(pricing, "completion")),
                supportedParameters.Any(x => x.Equals("response_format", StringComparison.OrdinalIgnoreCase)),
                supportedParameters.Any(x => x.Contains("tool", StringComparison.OrdinalIgnoreCase)),
                item.GetRawText()));
        }

        return modelos;
    }

    private async Task RecalcularRecomendacoesAsync(CancellationToken cancellationToken)
    {
        var modelos = await db.OpenRouterModelos.Where(x => x.Ativo).ToArrayAsync(cancellationToken);
        foreach (var modelo in modelos)
        {
            modelo.RecomendadoNormalizacao = false;
            modelo.RecomendadoAnalise = false;
            modelo.RecomendadoMensagem = false;
        }

        var normalizacao = EscolherNormalizacao(modelos);
        var analise = EscolherAnalise(modelos);
        var mensagem = EscolherMensagem(modelos);

        if (normalizacao is not null)
        {
            normalizacao.RecomendadoNormalizacao = true;
        }

        if (analise is not null)
        {
            analise.RecomendadoAnalise = true;
        }

        if (mensagem is not null)
        {
            mensagem.RecomendadoMensagem = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private OpenRouterModelo? EscolherNormalizacao(IReadOnlyList<OpenRouterModelo> modelos)
    {
        return modelos
            .Where(x => x.Ativo && (x.ContextLength ?? 0) >= _modelosOptions.MinContextLengthNormalizacao)
            .OrderByDescending(x => x.SuportaJsonEstruturado)
            .ThenByDescending(x => x.CustoBeneficioScore)
            .ThenBy(x => CustoTotal(x))
            .FirstOrDefault()
            ?? modelos.OrderByDescending(x => x.CustoBeneficioScore).FirstOrDefault();
    }

    private OpenRouterModelo? EscolherAnalise(IReadOnlyList<OpenRouterModelo> modelos)
    {
        return modelos
            .Where(x => x.Ativo && (x.ContextLength ?? 0) >= _modelosOptions.MinContextLengthAnalise)
            .OrderByDescending(x => x.CustoBeneficioScore)
            .ThenByDescending(x => x.ContextLength ?? 0)
            .FirstOrDefault()
            ?? modelos.OrderByDescending(x => x.CustoBeneficioScore).FirstOrDefault();
    }

    private static OpenRouterModelo? EscolherMensagem(IReadOnlyList<OpenRouterModelo> modelos)
    {
        return modelos
            .Where(x => x.Ativo)
            .OrderBy(x => CustoTotal(x))
            .ThenByDescending(x => x.CustoBeneficioScore)
            .FirstOrDefault();
    }

    private static decimal CalcularScore(OpenRouterModelo modelo)
    {
        var custo = CustoTotal(modelo);
        var custoScore = custo <= 0 ? 25m : Math.Min(25m, 25m / (1m + custo));
        var contextoScore = Math.Min(25m, ((modelo.ContextLength ?? 0) / 128_000m) * 25m);
        var jsonScore = modelo.SuportaJsonEstruturado ? 15m : 5m;
        var confiabilidade = modelo.TotalExecucoes == 0 ? 20m : (modelo.TotalSucessos / (decimal)modelo.TotalExecucoes) * 20m;
        var velocidade = modelo.TempoMedioMs is null ? 10m : Math.Max(0m, 10m - ((decimal)modelo.TempoMedioMs.Value / 10_000m));

        return Math.Round(custoScore + contextoScore + jsonScore + confiabilidade + velocidade, 4);
    }

    private static decimal CustoTotal(OpenRouterModelo modelo)
    {
        return (modelo.PrecoInputPorMilhaoTokens ?? 0) + (modelo.PrecoOutputPorMilhaoTokens ?? 0);
    }

    private static decimal? CalcularCusto(OpenRouterExecucao execucao, OpenRouterModelo modelo)
    {
        if (execucao.TokensInput is null && execucao.TokensOutput is null)
        {
            return null;
        }

        var input = ((execucao.TokensInput ?? 0) / 1_000_000m) * (modelo.PrecoInputPorMilhaoTokens ?? 0);
        var output = ((execucao.TokensOutput ?? 0) / 1_000_000m) * (modelo.PrecoOutputPorMilhaoTokens ?? 0);
        return Math.Round(input + output, 8);
    }

    private static OpenRouterModeloResponse ToResponse(OpenRouterModelo x)
    {
        return new OpenRouterModeloResponse(
            x.ModelId,
            x.Nome,
            x.Provider,
            x.ContextLength,
            x.PrecoInputPorMilhaoTokens,
            x.PrecoOutputPorMilhaoTokens,
            x.SuportaJsonEstruturado,
            x.SuportaTools,
            x.Ativo,
            x.RecomendadoNormalizacao,
            x.RecomendadoAnalise,
            x.RecomendadoMensagem,
            x.CustoBeneficioScore,
            x.TotalExecucoes,
            x.TotalSucessos,
            x.TotalFalhas,
            x.TempoMedioMs,
            x.UltimaAtualizacao);
    }

    private static OpenRouterModeloRecomendadoResponse ToRecomendado(OpenRouterTipoTarefa tipoTarefa, OpenRouterModelo? modelo, string motivo)
    {
        return new OpenRouterModeloRecomendadoResponse(tipoTarefa, modelo?.ModelId ?? "Nao configurado", modelo?.Nome, modelo?.CustoBeneficioScore ?? 0, motivo);
    }

    private static OpenRouterModeloConfiguracaoResponse ToConfiguracaoResponse(OpenRouterTipoTarefa tipoTarefa, OpenRouterModeloConfiguracao? config, OpenRouterModelo? modelo)
    {
        return new OpenRouterModeloConfiguracaoResponse(
            tipoTarefa,
            config?.ModelId,
            modelo?.Nome,
            config?.TravadoManual ?? false,
            config?.TravadoManual == true ? "Manual" : "Automatico",
            config?.Motivo,
            config?.AtualizadoEm);
    }

    private static OpenRouterModeloMetricaResponse CriarMetrica(OpenRouterTipoTarefa tipoTarefa, string modelId, string? nome, IEnumerable<OpenRouterExecucao> execucoes)
    {
        var lista = execucoes.ToArray();
        var total = lista.Length;
        var sucessos = lista.Count(x => x.Sucesso);
        var falhas = lista.Count(x => !x.Sucesso);
        var taxaSucesso = total == 0 ? 0 : Math.Round(sucessos / (decimal)total, 4);
        var tempoMedio = total == 0 ? 0 : Math.Round(lista.Average(x => x.TempoMs), 2);
        var custo = lista.Sum(x => x.CustoEstimado ?? 0);
        var score = CalcularScoreOperacional(total, taxaSucesso, tempoMedio, custo);

        return new OpenRouterModeloMetricaResponse(
            tipoTarefa,
            modelId,
            nome,
            total,
            sucessos,
            falhas,
            taxaSucesso,
            tempoMedio,
            lista.Sum(x => x.TokensInput ?? 0),
            lista.Sum(x => x.TokensOutput ?? 0),
            custo,
            score,
            total == 0 ? null : lista.Min(x => x.CriadoEm),
            total == 0 ? null : lista.Max(x => x.CriadoEm));
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

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result) ? result : null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ToPrecoPorMilhao(decimal? precoPorToken)
    {
        return precoPorToken is null ? null : Math.Round(precoPorToken.Value * 1_000_000m, 8);
    }

    private static string? ExtrairProvider(string modelId)
    {
        var index = modelId.IndexOf('/', StringComparison.Ordinal);
        return index <= 0 ? null : modelId[..index];
    }

    private sealed record ModeloOpenRouterDto(
        string ModelId,
        string? Nome,
        string? Provider,
        int? ContextLength,
        decimal? PrecoInputPorMilhaoTokens,
        decimal? PrecoOutputPorMilhaoTokens,
        bool SuportaJsonEstruturado,
        bool SuportaTools,
        string DadosJson);
}
