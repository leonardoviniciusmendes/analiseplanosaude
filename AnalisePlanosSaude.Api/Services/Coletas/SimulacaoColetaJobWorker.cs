using System.Text.Json;
using System.Text.RegularExpressions;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public sealed class SimulacaoColetaJobWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<PlaywrightOptions> playwrightOptions,
    ILogger<SimulacaoColetaJobWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CamposOperadora =
    [
        "operadora",
        "operadora_nome",
        "nome_operadora",
        "seguradora",
        "seguradora_nome",
        "administradora",
        "administradora_nome",
        "produto_operadora",
        "empresa"
    ];

    private static readonly string[] CamposTipoTabela =
    [
        "tipo_tabela",
        "tipoTabela",
        "tabela",
        "nome_tabela",
        "modalidade",
        "contratacao",
        "tipo_contratacao",
        "tipoContratacao",
        "categoria",
        "segmento"
    ];

    private static readonly IReadOnlyList<(string Nome, string[] Termos)> OperadorasConhecidas =
    [
        ("Amil", ["AMIL"]),
        ("Unimed", ["UNIMED"]),
        ("Assim", ["ASSIM"]),
        ("Bradesco Saude", ["BRADESCO SAUDE", "BRADESCO"]),
        ("SulAmerica", ["SULAMERICA", "SUL AMERICA"]),
        ("NotreDame Intermedica", ["NOTREDAME", "NOTRE DAME", "GNDI", "INTERMEDICA"]),
        ("Hapvida", ["HAPVIDA"]),
        ("Porto Seguro", ["PORTO SEGURO"]),
        ("MedSenior", ["MEDSENIOR"]),
        ("Prevent Senior", ["PREVENT SENIOR"]),
        ("Golden Cross", ["GOLDEN CROSS"])
    ];

    private static readonly SimulacaoJobTipo[] Ordem =
    [
        SimulacaoJobTipo.ColetarJsonPrincipal,
        SimulacaoJobTipo.ExtrairPlanosEValores,
        SimulacaoJobTipo.DescobrirEndpointRede,
        SimulacaoJobTipo.ColetarJsonRede,
        SimulacaoJobTipo.ExtrairRedeCredenciada,
        SimulacaoJobTipo.ConsolidarDados,
        SimulacaoJobTipo.AnalisarComIa
    ];

    private readonly PlaywrightOptions _playwrightOptions = playwrightOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var executed = await ExecutarProximoJobAsync(stoppingToken);
                await Task.Delay(executed ? 500 : 3000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha inesperada no worker de coletas.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<bool> ExecutarProximoJobAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var historico = scope.ServiceProvider.GetRequiredService<ISimulacaoHistoricoService>();
        await RecuperarJobsTravadosAsync(db, cancellationToken);

        var coletas = await db.SimulacoesColetas
            .Include(x => x.Jobs)
            .Where(x => x.Jobs.Any(j => j.Status == SimulacaoJobStatus.Pendente))
            .OrderBy(x => x.CriadoEm)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var coleta in coletas)
        {
            var job = ProximoJobExecutavel(coleta);
            if (job is null)
            {
                continue;
            }

            var coletaCompleta = await CarregarColetaParaJobAsync(db, coleta.Id, job.Tipo, cancellationToken);
            var jobCompleto = coletaCompleta.Jobs.First(x => x.Id == job.Id);
            await ExecutarJobAsync(db, historico, coletaCompleta, jobCompleto, cancellationToken);
            return true;
        }

        return false;
    }

    private static async Task RecuperarJobsTravadosAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var limite = DateTime.UtcNow.AddMinutes(-2);
        var travados = await db.SimulacoesJobs
            .Where(x => x.Status == SimulacaoJobStatus.Executando && x.IniciadoEm != null && x.IniciadoEm < limite)
            .ToListAsync(cancellationToken);

        foreach (var job in travados)
        {
            job.Status = job.Tentativas >= job.MaxTentativas ? SimulacaoJobStatus.Erro : SimulacaoJobStatus.Pendente;
            job.Erro = "Job recuperado após ficar preso em execução.";
            job.FinalizadoEm = DateTime.UtcNow;
        }

        if (travados.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<SimulacaoColeta> CarregarColetaParaJobAsync(AppDbContext db, Guid coletaId, SimulacaoJobTipo tipo, CancellationToken cancellationToken)
    {
        var query = db.SimulacoesColetas.Include(x => x.Jobs).AsQueryable();
        if (tipo is SimulacaoJobTipo.ConsolidarDados)
        {
            query = query
                .Include(x => x.Planos).ThenInclude(x => x.Prestadores)
                .Include(x => x.Planos).ThenInclude(x => x.ValoresFaixa);
        }
        else if (tipo is SimulacaoJobTipo.ExtrairRedeCredenciada)
        {
            query = query.Include(x => x.Planos);
        }
        else if (tipo is SimulacaoJobTipo.ExtrairPlanosEValores)
        {
            query = query.Include(x => x.Planos).ThenInclude(x => x.ValoresFaixa);
        }
        else
        {
            query = query.Include(x => x.Planos);
        }

        return await query.FirstAsync(x => x.Id == coletaId, cancellationToken);
    }

    private static SimulacaoJob? ProximoJobExecutavel(SimulacaoColeta coleta)
    {
        foreach (var tipo in Ordem)
        {
            var job = coleta.Jobs.FirstOrDefault(x => x.Tipo == tipo);
            if (job is null || job.Status != SimulacaoJobStatus.Pendente)
            {
                continue;
            }

            var index = Array.IndexOf(Ordem, tipo);
            var anterioresOk = Ordem.Take(index)
                .Select(tipoAnterior => coleta.Jobs.FirstOrDefault(x => x.Tipo == tipoAnterior))
                .Where(x => x is not null)
                .All(x => x!.Status is SimulacaoJobStatus.Concluido or SimulacaoJobStatus.Ignorado);
            return anterioresOk ? job : null;
        }

        return null;
    }

    private async Task ExecutarJobAsync(AppDbContext db, ISimulacaoHistoricoService historico, SimulacaoColeta coleta, SimulacaoJob job, CancellationToken cancellationToken)
    {
        job.Status = SimulacaoJobStatus.Executando;
        job.Tentativas++;
        job.IniciadoEm = DateTime.UtcNow;
        job.Erro = null;
        coleta.Status = StatusParaJob(job.Tipo);
        coleta.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            logger.LogInformation("Executando job {Job} da coleta {ColetaId}", job.Tipo, coleta.Id);
            switch (job.Tipo)
            {
                case SimulacaoJobTipo.ColetarJsonPrincipal:
                    await ColetarJsonPrincipalAsync(coleta, job, cancellationToken);
                    break;
                case SimulacaoJobTipo.ExtrairPlanosEValores:
                    await ExtrairPlanosEValoresAsync(db, coleta, job, cancellationToken);
                    break;
                case SimulacaoJobTipo.DescobrirEndpointRede:
                    await DescobrirEndpointRedeAsync(coleta, job, cancellationToken);
                    break;
                case SimulacaoJobTipo.ColetarJsonRede:
                    await ColetarJsonRedeAsync(coleta, job, cancellationToken);
                    break;
                case SimulacaoJobTipo.ExtrairRedeCredenciada:
                    await ExtrairRedeCredenciadaAsync(db, coleta, job, cancellationToken);
                    break;
                case SimulacaoJobTipo.ConsolidarDados:
                    Consolidar(coleta, job);
                    await historico.CriarVersaoAsync(coleta, cancellationToken);
                    break;
                case SimulacaoJobTipo.AnalisarComIa:
                    job.Status = SimulacaoJobStatus.Ignorado;
                    job.ResultadoJson = JsonSerializer.Serialize(new { motivo = "Análise com IA será disparada em etapa posterior." }, JsonOptions);
                    break;
            }

            if (job.Status == SimulacaoJobStatus.Executando)
            {
                job.Status = SimulacaoJobStatus.Concluido;
            }

            job.FinalizadoEm = DateTime.UtcNow;
            coleta.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erro no job {Job} da coleta {ColetaId}", job.Tipo, coleta.Id);
            job.Status = job.Tentativas >= job.MaxTentativas ? SimulacaoJobStatus.Erro : SimulacaoJobStatus.Pendente;
            job.Erro = ex.Message;
            job.FinalizadoEm = DateTime.UtcNow;
            coleta.Status = job.Status == SimulacaoJobStatus.Erro ? SimulacaoColetaStatus.ColetaConcluidaComErros : coleta.Status;
            coleta.Erro = ex.Message;
            coleta.AtualizadoEm = DateTime.UtcNow;
            await MarcarAtualizacaoComErroAsync(db, coleta.Id, ex.Message, job.Status == SimulacaoJobStatus.Erro, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task MarcarAtualizacaoComErroAsync(AppDbContext db, Guid coletaId, string erro, bool erroFinal, CancellationToken cancellationToken)
    {
        var atualizacao = await db.SimulacoesAtualizacoesJobs
            .Where(x => x.SimulacaoColetaId == coletaId && x.Status == SimulacaoAtualizacaoJobStatus.Executando)
            .OrderByDescending(x => x.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        if (atualizacao is null)
        {
            return;
        }

        atualizacao.Erro = erro;
        if (erroFinal)
        {
            atualizacao.Status = SimulacaoAtualizacaoJobStatus.Erro;
            atualizacao.FinalizadoEm = DateTime.UtcNow;
        }
    }

    private async Task ColetarJsonPrincipalAsync(SimulacaoColeta coleta, SimulacaoJob job, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        var json = await client.GetStringAsync(coleta.EndpointPrincipal, cancellationToken);
        JsonDocument.Parse(json);
        coleta.JsonPrincipal = json;
        job.ResultadoJson = JsonSerializer.Serialize(new { endpoint = coleta.EndpointPrincipal, bytes = json.Length }, JsonOptions);
    }

    private static async Task ExtrairPlanosEValoresAsync(AppDbContext db, SimulacaoColeta coleta, SimulacaoJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(coleta.JsonPrincipal))
        {
            throw new InvalidOperationException("JsonPrincipal não coletado.");
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM SimulacoesPlanos WHERE SimulacaoColetaId = {coleta.Id}", cancellationToken);

        using var doc = JsonDocument.Parse(coleta.JsonPrincipal);
        var item = doc.RootElement.GetProperty("itens")[0];
        var planosJson = item.GetProperty("planos").EnumerateArray().ToArray();
        var totais = item.GetProperty("valores").GetProperty("totais").EnumerateArray().Select(ParseDecimal).ToArray();
        var faixas = item.GetProperty("valores").GetProperty("faixas").EnumerateArray().ToArray();
        var idsExtraidos = planosJson
            .Select((planoJson, index) => JsonText(planoJson, "id") ?? $"indice-{index}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var idsJaExistentes = await db.SimulacoesPlanos.AsNoTracking()
            .Where(x => idsExtraidos.Contains(x.PlanoIdExterno))
            .Select(x => x.PlanoIdExterno)
            .ToListAsync(cancellationToken);
        var idsInseridosNestaExecucao = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var idsBloqueados = new HashSet<string>(idsJaExistentes, StringComparer.OrdinalIgnoreCase);
        var planosIgnorados = 0;

        for (var i = 0; i < planosJson.Length; i++)
        {
            var planoJson = planosJson[i];
            var planoIdExterno = JsonText(planoJson, "id") ?? $"indice-{i}";
            if (idsBloqueados.Contains(planoIdExterno) || !idsInseridosNestaExecucao.Add(planoIdExterno))
            {
                planosIgnorados++;
                continue;
            }

            var plano = new SimulacaoPlano
            {
                SimulacaoColetaId = coleta.Id,
                PlanoIdExterno = planoIdExterno,
                Operadora = ExtrairOperadora(planoJson, item, doc.RootElement),
                TipoTabela = ExtrairTipoTabela(planoJson, item, doc.RootElement),
                Nome = JsonText(planoJson, "nome") ?? $"Plano {i + 1}",
                ValorTotal = i < totais.Length ? totais[i] : null,
                DadosJson = planoJson.GetRawText()
            };

            foreach (var faixa in faixas)
            {
                var valores = faixa.GetProperty("planos").EnumerateArray().Select(ParseDecimal).ToArray();
                if (i >= valores.Length || valores[i] is null)
                {
                    continue;
                }

                var label = JsonText(faixa, "label") ?? "";
                var (min, max) = ParseFaixaIdade(label);
                plano.ValoresFaixa.Add(new SimulacaoValorFaixa
                {
                    Faixa = label,
                    IdadeMin = min,
                    IdadeMax = max,
                    Valor = valores[i]!.Value
                });
            }

            db.SimulacoesPlanos.Add(plano);
        }

        job.ResultadoJson = JsonSerializer.Serialize(new
        {
            planosEncontrados = planosJson.Length,
            planosInseridos = idsInseridosNestaExecucao.Count,
            planosIgnoradosPorDuplicidade = planosIgnorados,
            valores = faixas.Length * idsInseridosNestaExecucao.Count
        }, JsonOptions);
    }

    private async Task DescobrirEndpointRedeAsync(SimulacaoColeta coleta, SimulacaoJob job, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(coleta.EndpointRede))
        {
            job.ResultadoJson = JsonSerializer.Serialize(new { endpoint = coleta.EndpointRede, origem = "ja_informado" }, JsonOptions);
            return;
        }

        if (!string.IsNullOrWhiteSpace(coleta.JsonPrincipal))
        {
            var match = Regex.Match(coleta.JsonPrincipal, @"https:\/\/app\.simuladoronline\.com\/api\/pub\/simulador\/simulacao\/view\/[^""\s]+\/rede\/[^""\s]+", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                coleta.EndpointRede = match.Value.Contains('?') ? match.Value : $"{match.Value}?mode=vertical";
                job.ResultadoJson = JsonSerializer.Serialize(new { endpoint = coleta.EndpointRede, origem = "json_principal" }, JsonOptions);
                return;
            }
        }

        coleta.EndpointRede = await DescobrirEndpointRedeComPlaywrightAsync(coleta.UrlOriginal, cancellationToken);
        job.ResultadoJson = JsonSerializer.Serialize(new { endpoint = coleta.EndpointRede, origem = "playwright" }, JsonOptions);
    }

    private async Task<string> DescobrirEndpointRedeComPlaywrightAsync(string url, CancellationToken cancellationToken)
    {
        string? endpoint = null;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _playwrightOptions.Headless });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1366, Height = 900 } });
        page.SetDefaultTimeout(_playwrightOptions.TimeoutSeconds * 1000);

        page.Response += (_, response) =>
        {
            if (response.Url.Contains("/rede/", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = response.Url;
            }
        };

        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = _playwrightOptions.TimeoutSeconds * 1000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = _playwrightOptions.TimeoutSeconds * 1000 });
        await page.WaitForTimeoutAsync(1500);

        var botoes = page.Locator("button:has-text('hospitais'),[role=button]:has-text('hospitais'),a:has-text('hospitais')");
        if (await botoes.CountAsync() == 0)
        {
            await page.GetByText("AMIL", new PageGetByTextOptions { Exact = false }).First.ClickAsync(new LocatorClickOptions { Timeout = 5000, Force = true });
            await page.WaitForTimeoutAsync(1000);
        }

        botoes = page.Locator("button:has-text('hospitais'),[role=button]:has-text('hospitais'),a:has-text('hospitais')");
        if (await botoes.CountAsync() == 0)
        {
            throw new InvalidOperationException("Nenhum acionador de rede com texto hospitais foi encontrado.");
        }

        await botoes.First.ClickAsync(new LocatorClickOptions { Timeout = 10_000, Force = true });
        var started = DateTime.UtcNow;
        while (endpoint is null && DateTime.UtcNow - started < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
        }

        return endpoint ?? throw new InvalidOperationException("Endpoint /rede/ não foi capturado pelo Playwright.");
    }

    private async Task ColetarJsonRedeAsync(SimulacaoColeta coleta, SimulacaoJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(coleta.EndpointRede))
        {
            throw new InvalidOperationException("EndpointRede não descoberto.");
        }

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        var json = await client.GetStringAsync(coleta.EndpointRede, cancellationToken);
        JsonDocument.Parse(json);
        coleta.JsonRede = json;
        job.ResultadoJson = JsonSerializer.Serialize(new { endpoint = coleta.EndpointRede, bytes = json.Length }, JsonOptions);
    }

    private static async Task ExtrairRedeCredenciadaAsync(AppDbContext db, SimulacaoColeta coleta, SimulacaoJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(coleta.JsonRede))
        {
            throw new InvalidOperationException("JsonRede não coletado.");
        }

        foreach (var plano in coleta.Planos)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM SimulacoesPrestadores WHERE SimulacaoPlanoId = {plano.Id}", cancellationToken);
        }

        using var doc = JsonDocument.Parse(coleta.JsonRede);
        var redes = doc.RootElement.GetProperty("redes");
        var prestadoresParaInserir = new List<SimulacaoPrestador>();
        foreach (var redeProperty in redes.EnumerateObject())
        {
            var rede = redeProperty.Value;
            var planoId = JsonText(rede, "plano_id");
            var plano = coleta.Planos.FirstOrDefault(x => string.Equals(x.PlanoIdExterno, planoId, StringComparison.OrdinalIgnoreCase));
            if (plano is null)
            {
                continue;
            }

            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tipoProperty in rede.GetProperty("tipos").EnumerateObject())
            {
                var tipoNode = tipoProperty.Value;
                var tipo = NormalizarTipo(JsonText(tipoNode, "tipo") ?? "");
                foreach (var cidadeProperty in tipoNode.GetProperty("cidades").EnumerateObject())
                {
                    var cidadeNode = cidadeProperty.Value;
                    var cidade = JsonText(cidadeNode, "cidade");
                    var bairro = JsonText(cidadeNode, "zona");
                    var uf = InferirUf(cidadeProperty.Name);
                    foreach (var credenciado in cidadeNode.GetProperty("credenciados").EnumerateArray())
                    {
                        var nome = JsonText(credenciado, "credenciado_nome");
                        if (string.IsNullOrWhiteSpace(nome))
                        {
                            continue;
                        }

                        var key = $"{tipo}|{Normalizar(nome)}|{Normalizar(cidade)}|{Normalizar(bairro)}";
                        if (!dedupe.Add(key))
                        {
                            continue;
                        }

                        var atends = JsonText(credenciado, "atends");
                        var especialidades = string.IsNullOrWhiteSpace(atends)
                            ? Array.Empty<string>()
                            : atends.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        prestadoresParaInserir.Add(new SimulacaoPrestador
                        {
                            SimulacaoPlanoId = plano.Id,
                            Tipo = tipo,
                            Nome = nome,
                            Bairro = bairro,
                            Cidade = cidade,
                            Uf = uf,
                            EspecialidadesJson = JsonSerializer.Serialize(especialidades, JsonOptions),
                            TextoEvidencia = $"{tipo}: {nome}"
                        });
                    }
                }
            }
        }

        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            db.SimulacoesPrestadores.AddRange(prestadoresParaInserir);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        job.ResultadoJson = JsonSerializer.Serialize(new { prestadores = prestadoresParaInserir.Count }, JsonOptions);
    }

    private static void Consolidar(SimulacaoColeta coleta, SimulacaoJob job)
    {
        var jobsComErro = coleta.Jobs.Count(x => x.Status == SimulacaoJobStatus.Erro);
        coleta.Status = jobsComErro == 0 ? SimulacaoColetaStatus.ColetaConcluida : SimulacaoColetaStatus.ColetaConcluidaComErros;
        coleta.ProcessadoEm = DateTime.UtcNow;
        job.ResultadoJson = JsonSerializer.Serialize(new
        {
            planos = coleta.Planos.Count,
            prestadores = coleta.Planos.Sum(x => x.Prestadores.Count),
            status = coleta.Status.ToString()
        }, JsonOptions);
    }

    private static SimulacaoColetaStatus StatusParaJob(SimulacaoJobTipo tipo)
    {
        return tipo switch
        {
            SimulacaoJobTipo.ColetarJsonPrincipal => SimulacaoColetaStatus.ColetandoJsonPrincipal,
            SimulacaoJobTipo.ExtrairPlanosEValores => SimulacaoColetaStatus.ExtraindoPlanosEValores,
            SimulacaoJobTipo.DescobrirEndpointRede => SimulacaoColetaStatus.DescobrindoEndpointRede,
            SimulacaoJobTipo.ColetarJsonRede => SimulacaoColetaStatus.ColetandoJsonRede,
            SimulacaoJobTipo.ExtrairRedeCredenciada => SimulacaoColetaStatus.ExtraindoRedeCredenciada,
            SimulacaoJobTipo.ConsolidarDados => SimulacaoColetaStatus.ColetaConcluida,
            SimulacaoJobTipo.AnalisarComIa => SimulacaoColetaStatus.AnalisandoIa,
            _ => SimulacaoColetaStatus.Criada
        };
    }

    private static string? ExtrairOperadora(JsonElement planoJson, JsonElement item, JsonElement root)
    {
        var explicita = PrimeiroTextoEncontrado(CamposOperadora, planoJson, item, root);
        var operadoraExplicita = NormalizarValorLivre(explicita);
        if (!string.IsNullOrWhiteSpace(operadoraExplicita))
        {
            return operadoraExplicita;
        }

        var texto = TextoParaInferencia(planoJson, item, root);
        return InferirOperadoraConhecida(texto);
    }

    private static TipoTabelaPlano ExtrairTipoTabela(JsonElement planoJson, JsonElement item, JsonElement root)
    {
        var explicita = PrimeiroTextoEncontrado(CamposTipoTabela, planoJson, item, root);
        var tipoPorCampo = InferirTipoTabelaPorTexto(explicita);
        if (tipoPorCampo != TipoTabelaPlano.NaoInformado)
        {
            return tipoPorCampo;
        }

        return InferirTipoTabelaPorTexto(TextoParaInferencia(planoJson, item, root));
    }

    private static string? PrimeiroTextoEncontrado(IReadOnlyCollection<string> propriedades, params JsonElement[] fontes)
    {
        var nomes = propriedades.Select(NormalizarNomePropriedade).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var fonte in fontes)
        {
            var value = ProcurarTextoPorPropriedade(fonte, nomes, 0, 5);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ProcurarTextoPorPropriedade(JsonElement element, HashSet<string> nomes, int profundidade, int profundidadeMaxima)
    {
        if (profundidade > profundidadeMaxima)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (nomes.Contains(NormalizarNomePropriedade(property.Name)))
                {
                    var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                var nested = ProcurarTextoPorPropriedade(property.Value, nomes, profundidade + 1, profundidadeMaxima);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ProcurarTextoPorPropriedade(item, nomes, profundidade + 1, profundidadeMaxima);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string TextoParaInferencia(params JsonElement[] fontes)
    {
        var textos = new List<string>();
        foreach (var fonte in fontes)
        {
            ColetarTextos(fonte, textos, 0, 4);
        }

        return string.Join(' ', textos.Where(x => !string.IsNullOrWhiteSpace(x)).Take(300));
    }

    private static void ColetarTextos(JsonElement element, List<string> textos, int profundidade, int profundidadeMaxima)
    {
        if (profundidade > profundidadeMaxima || textos.Count >= 300)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    var text = ValorComoTexto(property.Value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textos.Add(text);
                    }
                }
                else
                {
                    ColetarTextos(property.Value, textos, profundidade + 1, profundidadeMaxima);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ColetarTextos(item, textos, profundidade + 1, profundidadeMaxima);
            }
        }
    }

    private static string? InferirOperadoraConhecida(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var normalizado = Normalizar(texto);
        var encontradas = OperadorasConhecidas
            .Where(x => x.Termos.Any(termo => normalizado.Contains(Normalizar(termo), StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.Nome)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return encontradas.Length == 1 ? encontradas[0] : null;
    }

    private static TipoTabelaPlano InferirTipoTabelaPorTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return TipoTabelaPlano.NaoInformado;
        }

        var normalizado = Normalizar(texto);
        var adesao = normalizado.Contains("ADESAO", StringComparison.OrdinalIgnoreCase);
        var empresarial = normalizado.Contains("PME", StringComparison.OrdinalIgnoreCase)
            || normalizado.Contains("PMP", StringComparison.OrdinalIgnoreCase)
            || normalizado.Contains("EMPRESARIAL", StringComparison.OrdinalIgnoreCase)
            || normalizado.Contains("EMPRESA", StringComparison.OrdinalIgnoreCase);

        if (adesao && empresarial)
        {
            return TipoTabelaPlano.AdesaoPmeEmpresarial;
        }

        if (empresarial)
        {
            return TipoTabelaPlano.PmeEmpresarial;
        }

        if (adesao)
        {
            return TipoTabelaPlano.Adesao;
        }

        if (normalizado.Contains("FAMILIAR", StringComparison.OrdinalIgnoreCase))
        {
            return TipoTabelaPlano.Familiar;
        }

        if (normalizado.Contains("INDIVIDUAL", StringComparison.OrdinalIgnoreCase))
        {
            return TipoTabelaPlano.Individual;
        }

        return TipoTabelaPlano.NaoInformado;
    }

    private static string? NormalizarValorLivre(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = Regex.Replace(value.Trim(), @"\s+", " ");
        return clean.Length > 160 ? clean[..160] : clean;
    }

    private static string NormalizarNomePropriedade(string value)
    {
        return Normalizar(value).Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
    }

    private static string? ValorComoTexto(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? JsonText(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static decimal? ParseDecimal(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(element.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static (int? Min, int? Max) ParseFaixaIdade(string label)
    {
        var numbers = Regex.Matches(label, @"\d+").Select(x => int.Parse(x.Value)).ToArray();
        return numbers.Length switch
        {
            >= 2 => (numbers[0], numbers[1]),
            1 when label.Contains("+", StringComparison.OrdinalIgnoreCase) => (numbers[0], null),
            1 => (numbers[0], numbers[0]),
            _ => (null, null)
        };
    }

    private static string NormalizarTipo(string tipo)
    {
        var normalized = Normalizar(tipo);
        if (normalized.Contains("hospita", StringComparison.OrdinalIgnoreCase))
        {
            return "Hospital";
        }

        if (normalized.Contains("clinica", StringComparison.OrdinalIgnoreCase))
        {
            return "Clinica";
        }

        if (normalized.Contains("laboratorio", StringComparison.OrdinalIgnoreCase))
        {
            return "Laboratorio";
        }

        if (normalized.Contains("diagnostico", StringComparison.OrdinalIgnoreCase))
        {
            return "Centro de Diagnostico";
        }

        if (normalized.Contains("pronto", StringComparison.OrdinalIgnoreCase))
        {
            return "Pronto-Socorro";
        }

        return "Outro";
    }

    private static string? InferirUf(string key)
    {
        var match = Regex.Match(key, @"[A-Z]{2}$");
        return match.Success ? match.Value : null;
    }

    private static string Normalizar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Trim().ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
    }
}
