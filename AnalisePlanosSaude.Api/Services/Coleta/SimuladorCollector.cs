using System.Text.Json;
using System.Text.RegularExpressions;
using AnalisePlanosSaude.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AnalisePlanosSaude.Api.Services.Coleta;

public sealed class SimuladorCollector(IOptions<PlaywrightOptions> options, ILogger<SimuladorCollector> logger) : ISimuladorCollector
{
    private readonly PlaywrightOptions _options = options.Value;

    public async Task<ColetaLinkResult> ColetarAsync(string url, CancellationToken cancellationToken)
    {
        var respostas = new List<RespostaRedeColetada>();
        var elementos = new List<string>();
        var secoes = new List<string>();
        var redesPorPlano = new List<RedePlanoColetada>();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = false,
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(_options.TimeoutSeconds * 1000);
        page.SetDefaultNavigationTimeout(_options.TimeoutSeconds * 1000);

        page.Response += async (_, response) =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var request = response.Request;
                if (request.ResourceType is not ("xhr" or "fetch"))
                {
                    return;
                }

                var responseHost = new Uri(response.Url).Host;
                if (!responseHost.Equals("app.simuladoronline.com", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var contentType = response.Headers.TryGetValue("content-type", out var value) ? value : "";
                if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var body = await response.TextAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    return;
                }

                var maxBodyLength = response.Url.Contains("/rede/", StringComparison.OrdinalIgnoreCase)
                    ? Math.Max(_options.MaxContentLength, 8_000_000)
                    : _options.MaxContentLength;

                respostas.Add(new RespostaRedeColetada(
                    response.Url,
                    request.Method,
                    response.Status,
                    request.ResourceType,
                    contentType,
                    Limit(body, maxBodyLength)));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Falha ao capturar resposta de rede.");
            }
        };

        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = _options.TimeoutSeconds * 1000
        });

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
        {
            Timeout = _options.TimeoutSeconds * 1000
        });

        await page.WaitForTimeoutAsync(1500);
        await FecharModaisAtualizacaoAsync(page);
        await AbrirTabelaSimulacaoAsync(page);
        await CapturarElementosAsync(page, elementos);
        if (!respostas.Any(x => x.Url.Contains("/api/pub/simulador/simulacao/view/", StringComparison.OrdinalIgnoreCase)
                && !x.Url.Contains("/rede/", StringComparison.OrdinalIgnoreCase)))
        {
            respostas.AddRange(await TentarEndpointPublicoObservadoAsync(page, url));
        }

        redesPorPlano.AddRange(await ColetarRedesPorPlanoAsync(page, respostas, secoes, cancellationToken));
        await AbrirTabelaSimulacaoAsync(page);
        await ExpandirSecoesAsync(page, secoes);
        await page.WaitForTimeoutAsync(1000);

        var conteudo = await page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions
        {
            Timeout = 10_000
        });

        if (respostas.Count == 0)
        {
            respostas.AddRange(await TentarEndpointPublicoObservadoAsync(page, url));
        }

        return new ColetaLinkResult(
            url,
            Limit(conteudo, _options.MaxContentLength),
            respostas,
            elementos.Distinct().Take(300).ToArray(),
            secoes.Distinct().Take(200).ToArray(),
            redesPorPlano);
    }

    private static async Task FecharModaisAtualizacaoAsync(IPage page)
    {
        await RemoverModalAtualizacaoAsync(page);

        foreach (var texto in new[] { "ATUALIZAR", "Fechar", "Cancelar", "close" })
        {
            try
            {
                var locator = page.GetByText(texto, new PageGetByTextOptions { Exact = false }).First;
                if (await locator.IsVisibleAsync())
                {
                    await page.Keyboard.PressAsync("Escape");
                    await page.WaitForTimeoutAsync(300);
                }
            }
            catch
            {
                await page.Keyboard.PressAsync("Escape");
            }
        }

        await RemoverModalAtualizacaoAsync(page);
    }

    private static async Task CapturarElementosAsync(IPage page, List<string> elementos)
    {
        var texts = await page.Locator("h1,h2,h3,h4,button,[role=button],a,[role=tab]").EvaluateAllAsync<string[]>(
            "els => els.map(e => (e.innerText || e.textContent || e.getAttribute('title') || e.getAttribute('aria-label') || '').trim()).filter(Boolean)");
        elementos.AddRange(texts);
    }

    private static async Task AbrirTabelaSimulacaoAsync(IPage page)
    {
        if (await PaginaContemAsync(page, "Valores"))
        {
            return;
        }

        var candidatos = page.Locator("button,[role=button],a");
        var count = Math.Min(await candidatos.CountAsync(), 20);
        for (var i = 0; i < count; i++)
        {
            try
            {
                var item = candidatos.Nth(i);
                var text = (await item.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1000 })).Trim();
                if (!await item.IsVisibleAsync())
                {
                    continue;
                }

                if (!text.Contains("assignment_ind", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("keyboard_arrow_down", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("AMIL", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("Simulação", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("Simulacao", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await item.ClickAsync(new LocatorClickOptions { Timeout = 3000, Force = true });
                await page.WaitForTimeoutAsync(800);
                if (await PaginaContemAsync(page, "Valores"))
                {
                    return;
                }
            }
            catch
            {
                // Continua procurando outro acionador textual da tabela.
            }
        }

        try
        {
            await page.GetByText("AMIL", new PageGetByTextOptions { Exact = false }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = 3000,
                Force = true
            });
            await page.WaitForTimeoutAsync(800);
        }
        catch
        {
            // Se continuar fechado, a etapa de mapeamento registrara a ausencia de colunas.
        }
    }

    private static async Task<bool> PaginaContemAsync(IPage page, string texto)
    {
        var body = await page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3000 });
        return body.Contains(texto, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExpandirSecoesAsync(IPage page, List<string> secoes)
    {
        var nomes = new[]
        {
            "Reembolso",
            "Elegibilidade",
            "CARÊNCIA",
            "Carência",
            "COPARTICIPAÇÃO",
            "COPARTICIPAÇÃO PARCIAL PARA TERAPIAS",
            "DOCUMENTAÇÃO NECESSÁRIA",
            "ÁREA DE COMERCIALIZAÇÃO",
            "ODONTOLOGIA"
        };

        foreach (var nome in nomes)
        {
            try
            {
                var locator = page.GetByText(nome, new PageGetByTextOptions { Exact = false }).First;
                if (await locator.IsVisibleAsync())
                {
                    await locator.ClickAsync(new LocatorClickOptions { Timeout = 3000, Force = true });
                    await page.WaitForTimeoutAsync(500);
                    var body = await page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3000 });
                    secoes.Add(Limit($"Secao clicada: {nome}\n{body}", 20_000));
                }
            }
            catch
            {
                secoes.Add($"Secao encontrada mas nao expandida automaticamente: {nome}");
            }
        }
    }

    private async Task<IReadOnlyList<RedePlanoColetada>> ColetarRedesPorPlanoAsync(IPage page, IReadOnlyList<RespostaRedeColetada> respostas, List<string> secoes, CancellationToken cancellationToken)
    {
        var planos = await IdentificarPlanosAsync(page, respostas);
        var redes = new List<RedePlanoColetada>();
        var botoesRede = await IdentificarBotoesRedeAsync(page);
        var count = Math.Min(planos.Count, botoesRede.Count);

        if (count == 0)
        {
            logger.LogWarning("Nao foi possivel identificar botoes de rede por plano. Planos={Planos}; Botoes={Botoes}", planos.Count, botoesRede.Count);
            secoes.Add($"Rede credenciada: nenhum botao de hospitais mapeado. Planos={planos.Count}; Botoes={botoesRede.Count}");
            return redes;
        }

        await AcionarEndpointRedeAsync(page, respostas, secoes, cancellationToken);
        var redesJson = ExtrairRedesPorPlanoDoJson(respostas, planos, botoesRede);
        if (redesJson.Count > 0)
        {
            foreach (var rede in redesJson)
            {
                var total = rede.Hospitais.Count + rede.Clinicas.Count + rede.Laboratorios.Count + rede.OutrosPrestadores.Count;
                logger.LogInformation("Rede extraida via JSON do plano {Plano}: {Quantidade} prestadores", rede.NomePlano, total);
                secoes.Add($"Rede credenciada JSON: {rede.NomePlano} - {rede.Acomodacao ?? "Nao informado"}; Elemento: {rede.ElementoClicado}; Prestadores: {total}");
            }

            return redesJson;
        }

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            redesJson = ExtrairRedesPorPlanoDoJson(respostas, planos, botoesRede);
            if (redesJson.Count > 0)
            {
                return RegistrarRedesJson(redesJson, secoes);
            }

            var plano = planos[i];
            var botao = botoesRede[i];
            logger.LogInformation("Processando rede credenciada do plano {Plano} - {Acomodacao}", plano.NomePlano, plano.Acomodacao ?? "Nao informado");

            try
            {
                await FecharModaisAtualizacaoAsync(page);
                await FecharModalRedeAsync(page);
                await AbrirTabelaSimulacaoAsync(page);
                await page.Keyboard.PressAsync("Escape");

                var botoesAtuais = await IdentificarBotoesRedeAsync(page);
                if (i >= botoesAtuais.Count)
                {
                    throw new InvalidOperationException($"Botao de rede nao encontrado para o indice {i}.");
                }

                botao = botoesAtuais[i];
                var item = page.Locator(botao.Selector).Nth(botao.Index);
                logger.LogInformation("Clicando elemento de rede do plano {Plano}: {Elemento}", plano.NomePlano, botao.Texto);
                await item.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = 5000 });
                await item.ClickAsync(new LocatorClickOptions { Timeout = 7000, Force = true });
                await page.WaitForTimeoutAsync(800);
                await RemoverModalAtualizacaoAsync(page);

                if (!await ModalRedeVisivelAsync(page))
                {
                    await AbrirTabelaSimulacaoAsync(page);
                    botoesAtuais = await IdentificarBotoesRedeAsync(page);
                    if (i < botoesAtuais.Count)
                    {
                        botao = botoesAtuais[i];
                        item = page.Locator(botao.Selector).Nth(botao.Index);
                        await item.ClickAsync(new LocatorClickOptions { Timeout = 7000, Force = true });
                    }
                }

                var modal = await AguardarModalRedeAsync(page);
                logger.LogInformation("Modal de rede aberto para o plano {Plano}", plano.NomePlano);
                await PercorrerConteudoModalAsync(modal, page, cancellationToken);

                var modalText = await modal.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 10_000 });
                var prestadores = ExtrairPrestadores(modalText);
                var hospitais = prestadores.Where(x => x.Tipo.Equals("Hospital", StringComparison.OrdinalIgnoreCase)).ToArray();
                var clinicas = prestadores.Where(x => x.Tipo.Equals("Clinica", StringComparison.OrdinalIgnoreCase)).ToArray();
                var laboratorios = prestadores.Where(x => x.Tipo.Equals("Laboratorio", StringComparison.OrdinalIgnoreCase)).ToArray();
                var outros = prestadores.Where(x => x.Tipo is not ("Hospital" or "Clinica" or "Laboratorio")).ToArray();

                logger.LogInformation(
                    "Rede extraida do plano {Plano}: {Quantidade} prestadores",
                    plano.NomePlano,
                    prestadores.Count);

                redes.Add(new RedePlanoColetada(
                    plano.NomePlano,
                    plano.Acomodacao,
                    plano.ValorTotal,
                    botao.QuantidadeHospitais,
                    hospitais,
                    clinicas,
                    laboratorios,
                    outros,
                    botao.Texto,
                    null));

                secoes.Add(Limit($"Rede credenciada extraida: {plano.NomePlano} - {plano.Acomodacao ?? "Nao informado"}\nElemento: {botao.Texto}\nPrestadores: {prestadores.Count}\n{modalText}", 80_000));
                await FecharModalRedeAsync(page);

                redesJson = ExtrairRedesPorPlanoDoJson(respostas, planos, botoesRede);
                if (redesJson.Count > 0)
                {
                    return RegistrarRedesJson(redesJson, secoes);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erro ao abrir ou ler rede do plano {Plano}", plano.NomePlano);
                redes.Add(new RedePlanoColetada(
                    plano.NomePlano,
                    plano.Acomodacao,
                    plano.ValorTotal,
                    botao.QuantidadeHospitais,
                    [],
                    [],
                    [],
                    [],
                    botao.Texto,
                    ex.Message));
                secoes.Add($"Erro ao abrir ou ler rede do plano {plano.NomePlano} - {plano.Acomodacao ?? "Nao informado"}: {ex.Message}");
                await FecharModalRedeAsync(page);

                redesJson = ExtrairRedesPorPlanoDoJson(respostas, planos, botoesRede);
                if (redesJson.Count > 0)
                {
                    return RegistrarRedesJson(redesJson, secoes);
                }
            }
        }

        return redes;
    }

    private IReadOnlyList<RedePlanoColetada> RegistrarRedesJson(IReadOnlyList<RedePlanoColetada> redesJson, List<string> secoes)
    {
        foreach (var rede in redesJson)
        {
            var total = rede.Hospitais.Count + rede.Clinicas.Count + rede.Laboratorios.Count + rede.OutrosPrestadores.Count;
            logger.LogInformation("Rede extraida via JSON do plano {Plano}: {Quantidade} prestadores", rede.NomePlano, total);
            secoes.Add($"Rede credenciada JSON: {rede.NomePlano} - {rede.Acomodacao ?? "Nao informado"}; Elemento: {rede.ElementoClicado}; Prestadores: {total}");
        }

        return redesJson;
    }

    private async Task AcionarEndpointRedeAsync(IPage page, IReadOnlyList<RespostaRedeColetada> respostas, List<string> secoes, CancellationToken cancellationToken)
    {
        await FecharModaisAtualizacaoAsync(page);
        await AbrirTabelaSimulacaoAsync(page);
        var botoes = await IdentificarBotoesRedeAsync(page);
        if (botoes.Count == 0)
        {
            return;
        }

        for (var i = 0; i < Math.Min(2, botoes.Count); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var botao = botoes[i];
                logger.LogInformation("Acionando endpoint de rede pelo elemento {Elemento}", botao.Texto);
                var item = page.Locator(botao.Selector).Nth(botao.Index);
                await item.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = 5000 });
                await item.ClickAsync(new LocatorClickOptions { Timeout = 7000, Force = true });
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20_000 });
                await page.WaitForTimeoutAsync(1500);
                await AguardarRespostaRedeCapturadaAsync(respostas, cancellationToken);
                await RemoverModalAtualizacaoAsync(page);
                secoes.Add($"Endpoint de rede acionado pelo elemento: {botao.Texto}");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erro ao acionar endpoint de rede pelo indice {Indice}", i);
                secoes.Add($"Erro ao acionar endpoint de rede no indice {i}: {ex.Message}");
            }
        }
    }

    private static async Task AguardarRespostaRedeCapturadaAsync(IReadOnlyList<RespostaRedeColetada> respostas, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (respostas.Any(x => x.Url.Contains("/rede/", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static IReadOnlyList<RedePlanoColetada> ExtrairRedesPorPlanoDoJson(
        IReadOnlyList<RespostaRedeColetada> respostas,
        IReadOnlyList<PlanoColuna> planos,
        IReadOnlyList<BotaoRede> botoesRede)
    {
        var response = respostas.LastOrDefault(x => x.Url.Contains("/rede/", StringComparison.OrdinalIgnoreCase));
        if (response is null || string.IsNullOrWhiteSpace(response.Body))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (!doc.RootElement.TryGetProperty("redes", out var redesElement) || redesElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var result = new List<RedePlanoColetada>();
            var index = 0;
            foreach (var redeProperty in redesElement.EnumerateObject())
            {
                var rede = redeProperty.Value;
                if (rede.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var planoNomeJson = rede.TryGetProperty("plano", out var planoProperty) ? JsonText(planoProperty) : null;
                var planoIdJson = rede.TryGetProperty("plano_id", out var planoIdProperty) ? JsonText(planoIdProperty) : null;
                var plano = !string.IsNullOrWhiteSpace(planoIdJson)
                    ? planos.FirstOrDefault(x => string.Equals(x.PlanoId, planoIdJson, StringComparison.OrdinalIgnoreCase))
                    : null;
                plano ??= index < planos.Count
                    ? planos[index]
                    : new PlanoColuna(planoNomeJson ?? $"Plano {index + 1}", null, null, null);
                var nomePlano = string.IsNullOrWhiteSpace(planoNomeJson) ? plano.NomePlano : planoNomeJson;
                var botao = index < botoesRede.Count ? botoesRede[index] : null;
                var prestadores = ExtrairPrestadoresDoJsonRede(rede);

                result.Add(new RedePlanoColetada(
                    nomePlano,
                    plano.Acomodacao,
                    plano.ValorTotal,
                    botao?.QuantidadeHospitais,
                    prestadores.Where(x => x.Tipo.Equals("Hospital", StringComparison.OrdinalIgnoreCase)).ToArray(),
                    prestadores.Where(x => x.Tipo.Equals("Clinica", StringComparison.OrdinalIgnoreCase)).ToArray(),
                    prestadores.Where(x => x.Tipo.Equals("Laboratorio", StringComparison.OrdinalIgnoreCase)).ToArray(),
                    prestadores.Where(x => x.Tipo is not ("Hospital" or "Clinica" or "Laboratorio")).ToArray(),
                    botao?.Texto,
                    null));

                index++;
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<PrestadorColetado> ExtrairPrestadoresDoJsonRede(JsonElement rede)
    {
        var prestadores = new Dictionary<string, PrestadorColetado>(StringComparer.OrdinalIgnoreCase);
        if (!rede.TryGetProperty("tipos", out var tipos) || tipos.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var tipoProperty in tipos.EnumerateObject())
        {
            var tipoNode = tipoProperty.Value;
            if (tipoNode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tipoTexto = tipoNode.TryGetProperty("tipo", out var tipoValue) ? JsonText(tipoValue) ?? "" : "";
            var tipo = NormalizarTipoRede(tipoTexto);

            if (!tipoNode.TryGetProperty("cidades", out var cidades) || cidades.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var cidadeProperty in cidades.EnumerateObject())
            {
                var cidadeNode = cidadeProperty.Value;
                if (cidadeNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var cidade = cidadeNode.TryGetProperty("cidade", out var cidadeValue) ? JsonText(cidadeValue) : null;
                var zona = cidadeNode.TryGetProperty("zona", out var zonaValue) ? JsonText(zonaValue) : null;
                var uf = InferirUf(cidadeProperty.Name, cidadeNode);

                if (!cidadeNode.TryGetProperty("credenciados", out var credenciados) || credenciados.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var credenciado in credenciados.EnumerateArray())
                {
                    if (credenciado.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var nome = credenciado.TryGetProperty("credenciado_nome", out var nomeValue) ? JsonText(nomeValue) : null;
                    if (string.IsNullOrWhiteSpace(nome))
                    {
                        continue;
                    }

                    var atends = credenciado.TryGetProperty("atends", out var atendsValue) ? JsonText(atendsValue) : null;
                    var especialidades = string.IsNullOrWhiteSpace(atends)
                        ? Array.Empty<string>()
                        : atends.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var key = $"{RemoveAcentos(nome)}|{tipo}|{RemoveAcentos(cidade ?? "")}|{RemoveAcentos(zona ?? "")}";

                    prestadores.TryAdd(key, new PrestadorColetado(
                        nome,
                        tipo,
                        zona,
                        cidade,
                        uf,
                        null,
                        especialidades));
                }
            }
        }

        return prestadores.Values.ToArray();
    }

    private static string NormalizarTipoRede(string tipoTexto)
    {
        var tipoNormalizado = RemoveAcentos(tipoTexto).ToLowerInvariant();
        if (tipoNormalizado.Contains("hospita", StringComparison.OrdinalIgnoreCase))
        {
            return "Hospital";
        }

        if (tipoNormalizado.Contains("clinica", StringComparison.OrdinalIgnoreCase))
        {
            return "Clinica";
        }

        if (tipoNormalizado.Contains("laboratorio", StringComparison.OrdinalIgnoreCase))
        {
            return "Laboratorio";
        }

        if (tipoNormalizado.Contains("diagnostico", StringComparison.OrdinalIgnoreCase))
        {
            return "Centro de Diagnostico";
        }

        if (tipoNormalizado.Contains("pronto", StringComparison.OrdinalIgnoreCase))
        {
            return "Pronto-Socorro";
        }

        if (tipoTexto.Contains("hospital", StringComparison.OrdinalIgnoreCase))
        {
            return "Hospital";
        }

        if (tipoTexto.Contains("clínica", StringComparison.OrdinalIgnoreCase) || tipoTexto.Contains("clinica", StringComparison.OrdinalIgnoreCase))
        {
            return "Clinica";
        }

        if (tipoTexto.Contains("laboratório", StringComparison.OrdinalIgnoreCase) || tipoTexto.Contains("laboratorio", StringComparison.OrdinalIgnoreCase))
        {
            return "Laboratorio";
        }

        if (tipoTexto.Contains("diagnóstico", StringComparison.OrdinalIgnoreCase) || tipoTexto.Contains("diagnostico", StringComparison.OrdinalIgnoreCase))
        {
            return "Centro de Diagnostico";
        }

        if (tipoTexto.Contains("pronto", StringComparison.OrdinalIgnoreCase))
        {
            return "Pronto-Socorro";
        }

        return "Outro";
    }

    private static string? InferirUf(string cidadeKey, JsonElement cidadeNode)
    {
        if (cidadeNode.TryGetProperty("uf", out var ufValue))
        {
            return JsonText(ufValue);
        }

        var match = Regex.Match(cidadeKey, @"[A-Z]{2}$");
        return match.Success ? match.Value : null;
    }

    private static string? JsonText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            _ => element.ToString()
        };
    }

    private static async Task<IReadOnlyList<PlanoColuna>> IdentificarPlanosAsync(IPage page, IReadOnlyList<RespostaRedeColetada> respostas)
    {
        var planosJson = IdentificarPlanosPeloJson(respostas);
        if (planosJson.Count > 0)
        {
            return planosJson;
        }

        var bodyText = await page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 10_000 });
        var lines = bodyText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valoresLine = lines.FirstOrDefault(x => x.StartsWith("Valores\t", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(valoresLine))
        {
            return [];
        }

        var nomes = valoresLine.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToArray();
        var acomodacoes = ExtrairLinhaTabela(lines, "Acomodação", "Acomodacao");
        var totais = ExtrairLinhaTabela(lines, "Total");
        var planos = new List<PlanoColuna>();

        for (var i = 0; i < nomes.Length; i++)
        {
            var nome = nomes[i];
            var acomodacao = i < acomodacoes.Count ? acomodacoes[i] : null;
            var valorTotal = i < totais.Count ? ParseDecimalBr(totais[i]) : null;
            planos.Add(new PlanoColuna(NomePlanoComAcomodacaoQuandoDuplicado(nome, acomodacao, nomes), acomodacao, valorTotal, null));
        }

        return planos;
    }

    private static IReadOnlyList<PlanoColuna> IdentificarPlanosPeloJson(IReadOnlyList<RespostaRedeColetada> respostas)
    {
        var principal = respostas.FirstOrDefault(x => x.Url.Contains("/api/pub/simulador/simulacao/view/", StringComparison.OrdinalIgnoreCase));
        if (principal is null || string.IsNullOrWhiteSpace(principal.Body))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(principal.Body);
            if (!doc.RootElement.TryGetProperty("itens", out var itens) || itens.ValueKind != JsonValueKind.Array || itens.GetArrayLength() == 0)
            {
                return [];
            }

            var item = itens[0];
            if (!item.TryGetProperty("planos", out var planosElement) || planosElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var totais = new List<decimal?>();
            if (item.TryGetProperty("valores", out var valores)
                && valores.TryGetProperty("totais", out var totaisElement)
                && totaisElement.ValueKind == JsonValueKind.Array)
            {
                totais.AddRange(totaisElement.EnumerateArray().Select(x => x.TryGetDecimal(out var value) ? value : (decimal?)null));
            }

            var acomodacoes = ExtrairAcomodacoesDoJson(item);
            var planosBase = planosElement.EnumerateArray()
                .Select(x => new
                {
                    Id = x.TryGetProperty("id", out var id) ? JsonText(id) : null,
                    Nome = x.TryGetProperty("nome", out var nome) ? nome.GetString() : null
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Nome))
                .ToArray();
            var nomesBase = planosBase.Select(x => x.Nome!).ToArray();

            var planos = new List<PlanoColuna>();
            for (var i = 0; i < planosBase.Length; i++)
            {
                var nome = planosBase[i].Nome!;
                var acomodacao = i < acomodacoes.Count ? acomodacoes[i] : null;
                var valorTotal = i < totais.Count ? totais[i] : null;
                planos.Add(new PlanoColuna(NomePlanoComAcomodacaoQuandoDuplicado(nome, acomodacao, nomesBase), acomodacao, valorTotal, planosBase[i].Id));
            }

            return planos;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string?> ExtrairAcomodacoesDoJson(JsonElement item)
    {
        var result = new List<string?>();
        foreach (var propertyName in new[] { "acomodacoes", "acomodacao", "acomodacoes_planos" })
        {
            if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in property.EnumerateArray())
            {
                result.Add(element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString());
            }

            return result;
        }

        return result;
    }

    private static IReadOnlyList<string> ExtrairLinhaTabela(IReadOnlyList<string> lines, params string[] labels)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!labels.Any(label => lines[i].StartsWith(label, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var sameLine = lines[i].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToArray();
            if (sameLine.Length > 0)
            {
                return sameLine;
            }

            var values = new List<string>();
            for (var j = i + 1; j < lines.Count && values.Count < 20; j++)
            {
                if (lines[j].Contains('\t') || lines[j].Contains(" anos", StringComparison.OrdinalIgnoreCase) || lines[j].Equals("Rede", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(lines[j]))
                {
                    values.Add(lines[j]);
                }
            }

            return values;
        }

        return [];
    }

    private static string NomePlanoComAcomodacaoQuandoDuplicado(string nome, string? acomodacao, IReadOnlyList<string> nomes)
    {
        if (nomes.Count(x => x.Equals(nome, StringComparison.OrdinalIgnoreCase)) <= 1 || string.IsNullOrWhiteSpace(acomodacao))
        {
            return nome;
        }

        return $"{nome} - {acomodacao}";
    }

    private static async Task<IReadOnlyList<BotaoRede>> IdentificarBotoesRedeAsync(IPage page)
    {
        const string selector = "button:has-text('hospitais'),[role=button]:has-text('hospitais'),a:has-text('hospitais')";
        return await page.Locator(selector).EvaluateAllAsync<BotaoRede[]>(
            @"els => els
                .map((e, index) => {
                    const text = (e.innerText || e.textContent || e.getAttribute('aria-label') || e.getAttribute('title') || '').trim();
                    const match = text.match(/(\d+)\s+hospitais?/i);
                    return match ? { selector: 'button,[role=button],a', index, texto: text, quantidadeHospitais: Number(match[1]) } : null;
                })
                .filter(Boolean)");
    }

    private static async Task<ILocator> AguardarModalRedeAsync(IPage page)
    {
        var modal = page.Locator("[role=dialog],.q-dialog,[aria-modal=true]").Last;
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.GetByText(new Regex("Rede Credenciada|REDE EXCLUSIVA", RegexOptions.IgnoreCase)).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20_000
        });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(1000);
        await AguardarFimSpinnerAsync(page);
        return modal;
    }

    private static async Task<bool> ModalRedeVisivelAsync(IPage page)
    {
        try
        {
            return await page.GetByText(new Regex("Rede Credenciada|REDE EXCLUSIVA", RegexOptions.IgnoreCase)).First.IsVisibleAsync();
        }
        catch
        {
            return false;
        }
    }

    private static async Task AguardarFimSpinnerAsync(IPage page)
    {
        try
        {
            await page.Locator("[role=progressbar],text=/carregando|aguarde|loading/i").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 10_000
            });
        }
        catch
        {
            // Nem todo modal usa spinner identificavel por role/texto.
        }
    }

    private static async Task PercorrerConteudoModalAsync(ILocator modal, IPage page, CancellationToken cancellationToken)
    {
        var lastTextLength = -1;
        for (var i = 0; i < 20; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ClicarCarregarMaisAsync(page);
            await modal.EvaluateAsync(@"el => {
                const scrollables = [el, ...Array.from(el.querySelectorAll('*'))]
                    .filter(x => x.scrollHeight && x.clientHeight && x.scrollHeight > x.clientHeight);
                for (const item of scrollables) item.scrollTop = item.scrollHeight;
            }");
            await page.WaitForTimeoutAsync(700);
            await AguardarFimSpinnerAsync(page);

            var text = await modal.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5000 });
            if (text.Length == lastTextLength)
            {
                break;
            }

            lastTextLength = text.Length;
        }
    }

    private static async Task ClicarCarregarMaisAsync(IPage page)
    {
        var carregarMais = page.GetByText(new Regex("carregar mais|mostrar mais|ver mais|próxima|proxima", RegexOptions.IgnoreCase)).First;
        try
        {
            if (await carregarMais.IsVisibleAsync())
            {
                await carregarMais.ClickAsync(new LocatorClickOptions { Timeout = 3000, Force = true });
                await page.WaitForTimeoutAsync(700);
            }
        }
        catch
        {
            // Paginação/mais resultados não existe em todos os modais.
        }
    }

    private static IReadOnlyList<PrestadorColetado> ExtrairPrestadores(string modalText)
    {
        var ignore = new Regex("^(rede|credenciada|hospital|hospitais|cl[ií]nica|cl[ií]nicas|laborat[oó]rio|laborat[oó]rios|fechar|buscar|pesquisar|filtro|todos|voltar|carregar mais|endere[cç]o)$", RegexOptions.IgnoreCase);
        var prestadores = new Dictionary<string, PrestadorColetado>(StringComparer.OrdinalIgnoreCase);
        var lines = modalText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => x.Length >= 3 && !ignore.IsMatch(x))
            .ToArray();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!PareceNomePrestador(line))
            {
                continue;
            }

            var contexto = string.Join(" ", lines.Skip(i).Take(5));
            var tipo = ClassificarTipo(contexto);
            var uf = Regex.Match(contexto, @"\b[A-Z]{2}\b").Value;
            var bairro = ExtrairCampo(contexto, "Bairro");
            var cidade = ExtrairCampo(contexto, "Cidade");
            var endereco = ExtrairEndereco(contexto);
            var especialidades = ExtrairEspecialidades(contexto);
            var key = $"{RemoveAcentos(line)}|{tipo}|{RemoveAcentos(endereco ?? "")}";

            prestadores.TryAdd(key, new PrestadorColetado(
                line,
                tipo,
                bairro,
                cidade,
                string.IsNullOrWhiteSpace(uf) ? null : uf,
                endereco,
                especialidades));
        }

        return prestadores.Values.ToArray();
    }

    private static bool PareceNomePrestador(string value)
    {
        if (value.Length < 4 || value.Length > 160)
        {
            return false;
        }

        if (Regex.IsMatch(value, @"^\d+$|^\d+\s+hospitais?$|^R\$", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(value, @"hospital|cl[ií]nica|laborat[oó]rio|diagn[oó]stico|centro|pronto|upa|casa de sa[uú]de|maternidade|instituto|medical|imagem|an[aá]lises", RegexOptions.IgnoreCase)
            || Regex.IsMatch(value, @"^[A-Z0-9 .,'&/-]{6,}$");
    }

    private static string ClassificarTipo(string contexto)
    {
        if (Regex.IsMatch(contexto, @"laborat[oó]rio|an[aá]lises cl[ií]nicas", RegexOptions.IgnoreCase))
        {
            return "Laboratorio";
        }

        if (Regex.IsMatch(contexto, @"cl[ií]nica|consult[oó]rio|centro m[eé]dico", RegexOptions.IgnoreCase))
        {
            return "Clinica";
        }

        if (Regex.IsMatch(contexto, @"hospital|maternidade|pronto[- ]?socorro|emerg[eê]ncia|casa de sa[uú]de", RegexOptions.IgnoreCase))
        {
            return "Hospital";
        }

        return Regex.IsMatch(contexto, @"diagn[oó]stico|imagem|radiologia|tomografia|resson[aâ]ncia", RegexOptions.IgnoreCase)
            ? "Centro de Diagnostico"
            : "Outro";
    }

    private static string? ExtrairCampo(string contexto, string campo)
    {
        var match = Regex.Match(contexto, $@"{campo}\s*:?\s*([^|,;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtrairEndereco(string contexto)
    {
        var match = Regex.Match(contexto, @"\b(rua|avenida|av\.|estrada|rodovia|travessa|praça|praca|alameda)\b[^|;]{5,120}", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : null;
    }

    private static IReadOnlyList<string> ExtrairEspecialidades(string contexto)
    {
        var especialidades = new[] { "Pronto-Socorro", "Emergência", "Pediatria", "Ortopedia", "Cardiologia", "Imagem", "Terapia", "Oncologia", "Maternidade" };
        return especialidades.Where(x => contexto.Contains(x, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static async Task FecharModalRedeAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);

        foreach (var texto in new[] { "Fechar", "close", "Voltar" })
        {
            try
            {
                var locator = page.GetByText(texto, new PageGetByTextOptions { Exact = false }).Last;
                if (await locator.IsVisibleAsync())
                {
                    await locator.ClickAsync(new LocatorClickOptions { Timeout = 3000, Force = true });
                    await page.WaitForTimeoutAsync(500);
                    return;
                }
            }
            catch
            {
                // Tenta Escape abaixo.
            }
        }

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task RemoverModalAtualizacaoAsync(IPage page)
    {
        try
        {
            await page.EvaluateAsync(@"() => {
                const texts = ['Nova versão disponível', 'Nova versao disponivel'];
                for (const el of Array.from(document.querySelectorAll('body *'))) {
                    const text = el.innerText || '';
                    if (texts.some(x => text.includes(x))) {
                        const dialog = el.closest('[role=dialog], .q-dialog, [id^=""q-portal""]') || el;
                        dialog.remove();
                    }
                }
                for (const el of Array.from(document.querySelectorAll('[class*=""backdrop""], [aria-hidden=""true""]'))) {
                    const style = getComputedStyle(el);
                    if (style.position === 'fixed') el.remove();
                }
                document.body.style.overflow = 'auto';
            }");
        }
        catch
        {
            // Melhor esforço para remover apenas modal de atualização que bloqueia cliques.
        }
    }

    private static decimal? ParseDecimalBr(string value)
    {
        var cleaned = Regex.Replace(value, @"[^\d,.-]", "").Replace(".", "").Replace(",", ".");
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string RemoveAcentos(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
    }

    private static async Task<IReadOnlyList<RespostaRedeColetada>> TentarEndpointPublicoObservadoAsync(IPage page, string url)
    {
        var token = new Uri(url).Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        var endpoint = $"https://app.simuladoronline.com/api/pub/simulador/simulacao/view/{token}";
        var json = await page.EvaluateAsync<string>(
            @"async endpoint => {
                const response = await fetch(endpoint);
                if (!response.ok) return '';
                return await response.text();
            }",
            endpoint);

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument.Parse(json);
        return [new RespostaRedeColetada(endpoint, "GET", 200, "xhr", "application/json", Limit(json, 1_000_000))];
    }

    private static string Limit(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed record PlanoColuna(string NomePlano, string? Acomodacao, decimal? ValorTotal, string? PlanoId);

    private sealed class BotaoRede
    {
        public string Selector { get; set; } = "";
        public int Index { get; set; }
        public string Texto { get; set; } = "";
        public int? QuantidadeHospitais { get; set; }
    }
}
