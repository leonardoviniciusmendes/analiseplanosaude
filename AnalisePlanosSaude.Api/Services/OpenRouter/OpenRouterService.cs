using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Options;
using AnalisePlanosSaude.Api.Services.Analise;
using AnalisePlanosSaude.Api.Services.Coleta;
using Microsoft.Extensions.Options;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public sealed class OpenRouterService(IHttpClientFactory httpClientFactory, IOptions<OpenRouterOptions> options) : IOpenRouterService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly OpenRouterOptions _options = options.Value;

    public async Task<IReadOnlyList<PlanoNormalizado>> NormalizarColetaAsync(string cep, IReadOnlyList<int> idades, ColetaLinkResult coleta, CancellationToken cancellationToken)
    {
        var prompt = """
        Você é um extrator de informações de simulações de planos de saúde.

        Analise exclusivamente os dados fornecidos.
        Extraia todos os planos encontrados e normalize os campos solicitados.

        Regras obrigatórias:
        1. Não invente informações.
        2. Não complete dados usando conhecimento externo.
        3. Não presuma que um hospital pertence à rede por conhecer a operadora.
        4. Diferencie hospitais, clínicas, laboratórios, centros de diagnóstico e prontos-socorros.
        5. Preserve valores monetários.
        6. Preserve os nomes completos dos planos.
        7. Relacione cada estabelecimento ao plano correto.
        8. Quando um campo não estiver disponível, retorne null ou "Não informado".
        9. Inclua evidências textuais curtas para os dados extraídos.
        10. Retorne somente JSON válido no formato: {"planos":[...]}.
        """;

        var payload = new
        {
            cep,
            idades,
            urlOrigem = coleta.Url,
            textosDaPagina = coleta.ConteudoPagina,
            chamadasJson = coleta.RespostasJson,
            elementosEncontrados = coleta.ElementosEncontrados,
            secoesEDetalhes = coleta.SecoesColetadas,
            contrato = "Cada item de planos deve seguir o contrato PlanoNormalizado: urlOrigem, operadora, plano, registroAns, valorTotal, valoresPorIdade, tipoContratacao, acomodacao, abrangencia, segmentacao, reembolso, elegibilidade, carencia, coparticipacao, coparticipacaoTerapias, documentacaoNecessaria, areaComercializacao, odontologia, hospitais, clinicas, laboratorios, centrosDiagnostico, prontosSocorros, observacoes, evidencias, camposNaoEncontrados."
        };

        var json = await ChatAsync(prompt, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        var wrapper = DeserializeOrThrow<PlanosWrapper>(json);
        return wrapper.Planos ?? [];
    }

    public async Task<ResultadoAnaliseResponse> CompararPlanosAsync(Guid analiseId, string status, string cep, IReadOnlyList<int> idades, IReadOnlyList<string> prioridades, string? observacoes, IReadOnlyList<PlanoNormalizado> planos, int quantidadeLinks, int linksComSucesso, int linksComErro, IReadOnlyList<string> avisos, CancellationToken cancellationToken)
    {
        var prompt = """
        Você é um corretor especialista em planos de saúde.

        Analise exclusivamente os planos fornecidos.

        Objetivos:
        1. identificar o melhor custo-benefício;
        2. identificar o plano mais econômico;
        3. identificar o plano com melhor rede;
        4. identificar a melhor opção para quem utiliza consultas e terapias;
        5. classificar os planos do melhor para o pior;
        6. explicar pontos positivos e pontos de atenção;
        7. elaborar roteiro para o corretor;
        8. elaborar mensagem pronta de WhatsApp;
        9. elaborar respostas para objeções.

        Regras:
        1. Não invente hospitais, clínicas, laboratórios ou coberturas.
        2. Não invente valores.
        3. Não esconda coparticipação, carências ou limitações.
        4. Menor preço não significa automaticamente melhor custo-benefício.
        5. Considere as idades, CEP, prioridades e observações.
        6. Não faça afirmações médicas.
        7. Não garanta autorização de procedimentos.
        8. Não prometa permanência futura da rede credenciada.
        9. Recomende confirmar rede, carência e elegibilidade antes da contratação.
        10. Informações ausentes devem ser "Não informado".
        11. Retorne somente JSON válido aderente ao contrato ResultadoAnaliseResponse.
        """;

        var payload = new
        {
            analiseId,
            status,
            entrada = new { cep, idades, quantidadeLinks, prioridades, observacoes },
            processamento = new { linksProcessados = quantidadeLinks, linksComSucesso, linksComErro, quantidadePlanos = planos.Count, avisos },
            planos
        };

        var json = await ChatAsync(prompt, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        return DeserializeOrThrow<ResultadoAnaliseResponse>(json);
    }

    private async Task<string> ChatAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new ValidacaoException("FALHA_OPENROUTER", "OpenRouter__ApiKey e OpenRouter__Model devem estar configurados.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, _options.TimeoutSeconds)));

        var client = httpClientFactory.CreateClient("OpenRouter");
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        client.DefaultRequestHeaders.Remove("HTTP-Referer");
        client.DefaultRequestHeaders.Remove("X-Title");
        client.DefaultRequestHeaders.Add("HTTP-Referer", _options.SiteUrl);
        client.DefaultRequestHeaders.Add("X-Title", _options.AppName);

        var request = new
        {
            model = _options.Model,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        using var response = await client.PostAsync("chat/completions", new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json"), timeout.Token);
        var content = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new ValidacaoException("FALHA_OPENROUTER", "Falha ao chamar OpenRouter.", [content]);
        }

        using var doc = JsonDocument.Parse(content);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return ExtractJson(message);
    }

    private static T DeserializeOrThrow<T>(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return parsed ?? throw new JsonException("Resposta vazia.");
        }
        catch (JsonException ex)
        {
            throw new ValidacaoException("RESPOSTA_IA_INVALIDA", "A resposta da IA não é um JSON válido no contrato esperado.", [ex.Message, json]);
        }
    }

    private static string ExtractJson(string value)
    {
        var cleaned = value.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                .Replace("```", "", StringComparison.Ordinal)
                .Trim();
        }

        var firstObject = cleaned.IndexOf('{');
        var firstArray = cleaned.IndexOf('[');
        var first = firstObject >= 0 && firstArray >= 0 ? Math.Min(firstObject, firstArray) : Math.Max(firstObject, firstArray);
        if (first > 0)
        {
            cleaned = cleaned[first..];
        }

        JsonDocument.Parse(cleaned);
        return cleaned;
    }

    private sealed record PlanosWrapper(IReadOnlyList<PlanoNormalizado>? Planos);
}
