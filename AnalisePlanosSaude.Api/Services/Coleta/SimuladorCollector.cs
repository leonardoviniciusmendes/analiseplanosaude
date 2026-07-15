using System.Text.Json;
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

                respostas.Add(new RespostaRedeColetada(
                    response.Url,
                    request.Method,
                    response.Status,
                    request.ResourceType,
                    contentType,
                    Limit(body, _options.MaxContentLength / 2)));
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
        await CapturarElementosAsync(page, elementos);
        await ExpandirSecoesAsync(page, secoes);
        await ClicarRedeAsync(page, secoes);
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
            secoes.Distinct().Take(200).ToArray());
    }

    private static async Task FecharModaisAtualizacaoAsync(IPage page)
    {
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
    }

    private static async Task CapturarElementosAsync(IPage page, List<string> elementos)
    {
        var texts = await page.Locator("h1,h2,h3,h4,button,[role=button],a,[role=tab]").EvaluateAllAsync<string[]>(
            "els => els.map(e => (e.innerText || e.textContent || e.getAttribute('title') || e.getAttribute('aria-label') || '').trim()).filter(Boolean)");
        elementos.AddRange(texts);
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

    private static async Task ClicarRedeAsync(IPage page, List<string> secoes)
    {
        var candidatos = page.Locator("button[title*='Rede'],button:has-text('hospitais'),[role=button]:has-text('hospitais')");
        var count = Math.Min(await candidatos.CountAsync(), 12);
        for (var i = 0; i < count; i++)
        {
            try
            {
                await page.Keyboard.PressAsync("Escape");
                var item = candidatos.Nth(i);
                var label = await item.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1500 });
                await item.ClickAsync(new LocatorClickOptions { Timeout = 5000, Force = true });
                await page.WaitForTimeoutAsync(1200);
                var body = await page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3000 });
                secoes.Add(Limit($"Rede clicada: {label}\n{body}", 40_000));
            }
            catch (Exception ex)
            {
                secoes.Add($"Rede nao clicada automaticamente no indice {i}: {ex.Message}");
            }
        }
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
}
