using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.Coleta;
using AnalisePlanosSaude.Api.Services.OpenRouter;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Services.Analise;

public sealed class AnaliseService(AppDbContext db, ISimuladorCollector collector, IOpenRouterService openRouter) : IAnaliseService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<ResultadoAnaliseResponse> CriarEProcessarAsync(CriarAnaliseRequest request, CancellationToken cancellationToken)
    {
        var entrada = await ValidarAsync(request, cancellationToken);
        var analise = new Entities.Analise
        {
            Cep = entrada.Cep,
            IdadesJson = JsonSerializer.Serialize(entrada.Idades, JsonOptions),
            PrioridadesJson = JsonSerializer.Serialize(entrada.Prioridades, JsonOptions),
            Observacoes = entrada.Observacoes,
            Status = AnaliseStatus.Pendente,
            Links = entrada.Links.Select(x => new AnaliseLink { Url = x, Status = AnaliseStatus.Pendente }).ToList()
        };

        db.Analises.Add(analise);
        await db.SaveChangesAsync(cancellationToken);

        return await ProcessarAsync(analise, cancellationToken);
    }

    public async Task<IReadOnlyList<AnaliseResumoResponse>> ListarAsync(CancellationToken cancellationToken)
    {
        var analises = await db.Analises.AsNoTracking()
            .Include(x => x.Links)
            .OrderByDescending(x => x.CriadoEm)
            .ToListAsync(cancellationToken);

        return analises.Select(x => new AnaliseResumoResponse(
            x.Id,
            x.Cep,
            ReadJson<IReadOnlyList<int>>(x.IdadesJson) ?? [],
            ReadJson<IReadOnlyList<string>>(x.PrioridadesJson) ?? [],
            x.Observacoes,
            x.Status,
            x.Links.Count,
            x.ResumoCorretor,
            x.MensagemCliente,
            x.Erro,
            x.CriadoEm,
            x.ProcessadoEm)).ToArray();
    }

    public async Task<AnaliseCompletaResponse> ObterAsync(Guid id, CancellationToken cancellationToken)
    {
        var analise = await BuscarAsync(id, cancellationToken);
        return ToCompleta(analise);
    }

    public async Task<ResultadoAnaliseResponse> ObterResultadoAsync(Guid id, CancellationToken cancellationToken)
    {
        var analise = await BuscarAsync(id, cancellationToken);
        if (string.IsNullOrWhiteSpace(analise.ResultadoJson))
        {
            throw new ValidacaoException("ANALISE_NAO_ENCONTRADA", "A análise ainda não possui resultado salvo.");
        }

        return ReadJson<ResultadoAnaliseResponse>(analise.ResultadoJson)
            ?? throw new ValidacaoException("RESPOSTA_IA_INVALIDA", "Resultado salvo está inválido.");
    }

    public async Task<ResultadoAnaliseResponse> ReprocessarAsync(Guid id, CancellationToken cancellationToken)
    {
        var analise = await BuscarAsync(id, cancellationToken);
        analise.Status = AnaliseStatus.Pendente;
        analise.ResultadoJson = null;
        analise.ResumoCorretor = null;
        analise.ScriptCorretor = null;
        analise.MensagemCliente = null;
        analise.Erro = null;
        analise.ProcessadoEm = null;
        foreach (var link in analise.Links)
        {
            link.Status = AnaliseStatus.Pendente;
            link.ConteudoPagina = null;
            link.DadosColetadosJson = null;
            link.Erro = null;
            link.ProcessadoEm = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ProcessarAsync(analise, cancellationToken);
    }

    public async Task RemoverAsync(Guid id, CancellationToken cancellationToken)
    {
        var analise = await BuscarAsync(id, cancellationToken);
        db.Analises.Remove(analise);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ResultadoAnaliseResponse> ProcessarAsync(Entities.Analise analise, CancellationToken cancellationToken)
    {
        analise.Status = AnaliseStatus.Processando;
        analise.Erro = null;
        await db.SaveChangesAsync(cancellationToken);

        var idades = ReadJson<IReadOnlyList<int>>(analise.IdadesJson) ?? [];
        var prioridades = ReadJson<IReadOnlyList<string>>(analise.PrioridadesJson) ?? [];
        var planos = new List<PlanoNormalizado>();
        var avisos = new List<string>();

        foreach (var link in analise.Links)
        {
            try
            {
                link.Status = AnaliseStatus.Processando;
                await db.SaveChangesAsync(cancellationToken);

                var coleta = await collector.ColetarAsync(link.Url, cancellationToken);
                link.ConteudoPagina = coleta.ConteudoPagina;

                IReadOnlyList<PlanoNormalizado> normalizados;
                try
                {
                    normalizados = await openRouter.NormalizarColetaAsync(analise.Cep, idades, coleta, cancellationToken);
                    normalizados = AplicarRedesColetadas(normalizados, coleta);
                }
                catch (Exception ex)
                {
                    avisos.Add($"Falha na normalização pelo OpenRouter no link {link.Url}: {ex.Message}. Resultado montado com os dados estruturados coletados pelo Playwright.");
                    normalizados = CriarPlanosAPartirDaRedeColetada(coleta);
                    if (normalizados.Count == 0)
                    {
                        throw;
                    }
                }

                planos.AddRange(normalizados);
                link.DadosColetadosJson = JsonSerializer.Serialize(new { coleta.RespostasJson, coleta.ElementosEncontrados, coleta.SecoesColetadas, coleta.RedesPorPlano, planos = normalizados }, JsonOptions);
                link.Status = AnaliseStatus.Concluido;
                link.ProcessadoEm = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                link.Status = AnaliseStatus.Erro;
                link.Erro = ex.Message;
                link.ProcessadoEm = DateTime.UtcNow;
                avisos.Add($"Falha no link {link.Url}: {ex.Message}");
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var linksComSucesso = analise.Links.Count(x => x.Status == AnaliseStatus.Concluido);
        var linksComErro = analise.Links.Count(x => x.Status == AnaliseStatus.Erro);
        var status = linksComErro == 0 ? AnaliseStatus.Concluido : linksComSucesso > 0 ? AnaliseStatus.ConcluidoComErros : AnaliseStatus.Erro;

        if (planos.Count == 0)
        {
            analise.Status = AnaliseStatus.Erro;
            analise.Erro = "Nenhum plano foi normalizado a partir dos links informados.";
            analise.ProcessadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw new ValidacaoException("FALHA_COLETA", analise.Erro, avisos);
        }

        var resultadoBase = CriarResultadoEstruturado(
            analise.Id,
            status.ToString(),
            analise.Cep,
            idades,
            prioridades,
            analise.Observacoes,
            planos,
            analise.Links.Count,
            linksComSucesso,
            linksComErro,
            avisos);

        ResultadoAnaliseResponse resultado;
        try
        {
            var resultadoIa = await openRouter.CompararPlanosAsync(
                analise.Id,
                status.ToString(),
                analise.Cep,
                idades,
                prioridades,
                analise.Observacoes,
                planos,
                analise.Links.Count,
                linksComSucesso,
                linksComErro,
                avisos,
                cancellationToken);
            resultado = CompletarResultado(resultadoIa, resultadoBase);
        }
        catch (Exception ex)
        {
            avisos.Add($"Falha na análise comercial pelo OpenRouter: {ex.Message}. Ranking montado com os dados estruturados coletados.");
            status = linksComErro == 0 ? AnaliseStatus.ConcluidoComErros : status;
            resultado = CriarResultadoEstruturado(
                analise.Id,
                status.ToString(),
                analise.Cep,
                idades,
                prioridades,
                analise.Observacoes,
                planos,
                analise.Links.Count,
                linksComSucesso,
                linksComErro,
                avisos);
        }

        analise.Status = status;
        analise.ResultadoJson = JsonSerializer.Serialize(resultado, JsonOptions);
        analise.ResumoCorretor = resultado.ResumoCorretor;
        analise.ScriptCorretor = resultado.ScriptCorretor;
        analise.MensagemCliente = resultado.MensagemCliente;
        analise.Erro = status == AnaliseStatus.Concluido ? null : string.Join(Environment.NewLine, avisos);
        analise.ProcessadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return resultado;
    }

    private static IReadOnlyList<PlanoNormalizado> AplicarRedesColetadas(IReadOnlyList<PlanoNormalizado> planos, ColetaLinkResult coleta)
    {
        if (planos.Count == 0)
        {
            return CriarPlanosAPartirDaRedeColetada(coleta);
        }

        if (coleta.RedesPorPlano.Count == 0)
        {
            return planos;
        }

        var redesNaoUsadas = coleta.RedesPorPlano.ToList();
        var enriquecidos = new List<PlanoNormalizado>();
        foreach (var plano in planos)
        {
            var rede = redesNaoUsadas.FirstOrDefault(x => NomeCompativel(plano.Plano, x.NomePlano));
            rede ??= redesNaoUsadas.FirstOrDefault(x => ValoresCompativeis(plano.ValorTotal, x.ValorTotal));
            if (rede is null)
            {
                enriquecidos.Add(plano);
                continue;
            }

            redesNaoUsadas.Remove(rede);
            enriquecidos.Add(plano with
            {
                Plano = string.IsNullOrWhiteSpace(plano.Plano) ? rede.NomePlano : plano.Plano,
                Acomodacao = string.IsNullOrWhiteSpace(plano.Acomodacao) ? rede.Acomodacao : plano.Acomodacao,
                ValorTotal = plano.ValorTotal <= 0 && rede.ValorTotal.HasValue ? rede.ValorTotal.Value : plano.ValorTotal,
                Hospitais = MapearPrestadores(rede.Hospitais, rede.NomePlano, coleta.Url),
                Clinicas = MapearPrestadores(rede.Clinicas, rede.NomePlano, coleta.Url),
                Laboratorios = MapearPrestadores(rede.Laboratorios, rede.NomePlano, coleta.Url),
                CentrosDiagnostico = MapearPrestadores(rede.OutrosPrestadores.Where(x => x.Tipo.Equals("Centro de Diagnostico", StringComparison.OrdinalIgnoreCase)).ToArray(), rede.NomePlano, coleta.Url),
                ProntosSocorros = MapearPrestadores(rede.OutrosPrestadores.Where(x => x.Tipo.Equals("Pronto-Socorro", StringComparison.OrdinalIgnoreCase)).ToArray(), rede.NomePlano, coleta.Url),
                Evidencias = Mesclar(plano.Evidencias, [$"Rede credenciada coletada pelo Playwright no elemento '{rede.ElementoClicado ?? "Rede"}' com {TotalPrestadores(rede)} prestadores."]),
                CamposNaoEncontrados = RemoverCamposEncontrados(plano.CamposNaoEncontrados, rede)
            });
        }

        enriquecidos.AddRange(redesNaoUsadas.Select(CriarPlanoFallback(coleta.Url)));
        return RemoverPlanosDuplicados(enriquecidos);
    }

    private static IReadOnlyList<PlanoNormalizado> CriarPlanosAPartirDaRedeColetada(ColetaLinkResult coleta)
    {
        return RemoverPlanosDuplicados(coleta.RedesPorPlano.Select(CriarPlanoFallback(coleta.Url)).ToArray());
    }

    private static Func<RedePlanoColetada, PlanoNormalizado> CriarPlanoFallback(string urlOrigem)
    {
        return rede => new PlanoNormalizado(
            urlOrigem,
            null,
            rede.NomePlano,
            null,
            rede.ValorTotal ?? 0,
            [],
            null,
            rede.Acomodacao,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            null,
            MapearPrestadores(rede.Hospitais, rede.NomePlano, urlOrigem),
            MapearPrestadores(rede.Clinicas, rede.NomePlano, urlOrigem),
            MapearPrestadores(rede.Laboratorios, rede.NomePlano, urlOrigem),
            MapearPrestadores(rede.OutrosPrestadores.Where(x => x.Tipo.Equals("Centro de Diagnostico", StringComparison.OrdinalIgnoreCase)).ToArray(), rede.NomePlano, urlOrigem),
            MapearPrestadores(rede.OutrosPrestadores.Where(x => x.Tipo.Equals("Pronto-Socorro", StringComparison.OrdinalIgnoreCase)).ToArray(), rede.NomePlano, urlOrigem),
            rede.Erro is null ? [] : [$"Erro ao coletar rede: {rede.Erro}"],
            [$"Rede credenciada coletada pelo Playwright no elemento '{rede.ElementoClicado ?? "Rede"}' com {TotalPrestadores(rede)} prestadores."],
            CamposNaoEncontradosFallback(rede));
    }

    private static ResultadoAnaliseResponse CriarResultadoEstruturado(
        Guid analiseId,
        string status,
        string cep,
        IReadOnlyList<int> idades,
        IReadOnlyList<string> prioridades,
        string? observacoes,
        IReadOnlyList<PlanoNormalizado> planos,
        int quantidadeLinks,
        int linksComSucesso,
        int linksComErro,
        IReadOnlyList<string> avisos)
    {
        var ranking = planos
            .OrderByDescending(QuantidadeRede)
            .ThenBy(x => x.ValorTotal <= 0 ? decimal.MaxValue : x.ValorTotal)
            .Select((plano, index) => CriarItemRanking(plano, index + 1))
            .ToArray();

        var maisEconomicoPlano = planos.Where(x => x.ValorTotal > 0).OrderBy(x => x.ValorTotal).FirstOrDefault();
        var melhorRedePlano = planos.OrderByDescending(QuantidadeRede).ThenBy(x => x.ValorTotal <= 0 ? decimal.MaxValue : x.ValorTotal).FirstOrDefault();
        var melhorCustoPlano = ranking.FirstOrDefault();
        var melhorCusto = melhorCustoPlano is null ? null : planos.FirstOrDefault(x => NomeCompativel(x.Plano, melhorCustoPlano.Plano)) ?? planos.FirstOrDefault();
        var terapiasPlano = planos
            .OrderByDescending(x => ContemTexto(x.CoparticipacaoTerapias, "Não informado") ? 0 : 1)
            .ThenByDescending(QuantidadeRede)
            .FirstOrDefault();

        var resumo = $"Análise gerada com base nos dados coletados da simulação para o CEP {cep} e idade(s) {string.Join(", ", idades)}. Foram encontrados {planos.Count} planos e {ranking.Sum(x => x.Hospitais.Count + x.Clinicas.Count + x.Laboratorios.Count)} prestadores classificados nas redes.";
        var script = CriarScriptComercial(melhorCusto, maisEconomicoPlano, melhorRedePlano);
        var mensagem = CriarMensagemCliente(cep, idades, melhorCusto, maisEconomicoPlano, melhorRedePlano);

        return new ResultadoAnaliseResponse(
            analiseId,
            status,
            new EntradaResultado(cep, idades, quantidadeLinks, prioridades, observacoes),
            new ProcessamentoResultado(quantidadeLinks, linksComSucesso, linksComErro, planos.Count, avisos),
            CriarDestaque(melhorCusto, "Melhor custo-benefício calculado considerando valor informado e quantidade de rede coletada."),
            CriarDestaque(maisEconomicoPlano, "Menor mensalidade informada entre os planos coletados."),
            CriarDestaque(melhorRedePlano, "Maior quantidade de prestadores coletados na rede credenciada."),
            CriarDestaque(terapiasPlano, "Opção destacada para terapias com base nos dados disponíveis; confirmar regras e coparticipação antes da contratação."),
            ranking,
            resumo,
            script,
            mensagem,
            CriarMensagemCurta(melhorCusto, maisEconomicoPlano, melhorRedePlano),
            CriarObjecoes(),
            avisos.Concat(["Resultado estruturado gerado sem inventar dados ausentes; confirme rede, carências, elegibilidade e coparticipação antes da contratação."]).Distinct().ToArray());
    }

    private static ResultadoAnaliseResponse CompletarResultado(ResultadoAnaliseResponse resultado, ResultadoAnaliseResponse fallback)
    {
        var ranking = fallback.Ranking;
        var alertas = Mesclar(resultado.Alertas ?? [], fallback.Alertas);

        return resultado with
        {
            AnaliseId = resultado.AnaliseId == Guid.Empty ? fallback.AnaliseId : resultado.AnaliseId,
            Status = string.IsNullOrWhiteSpace(resultado.Status) ? fallback.Status : resultado.Status,
            Entrada = fallback.Entrada,
            Processamento = fallback.Processamento,
            MelhorCustoBeneficio = CompletarDestaque(resultado.MelhorCustoBeneficio, fallback.MelhorCustoBeneficio),
            MaisEconomico = CompletarDestaque(resultado.MaisEconomico, fallback.MaisEconomico),
            MelhorRede = CompletarDestaque(resultado.MelhorRede, fallback.MelhorRede),
            MelhorParaTerapias = CompletarDestaque(resultado.MelhorParaTerapias, fallback.MelhorParaTerapias),
            Ranking = ranking,
            ResumoCorretor = string.IsNullOrWhiteSpace(resultado.ResumoCorretor) ? fallback.ResumoCorretor : resultado.ResumoCorretor,
            ScriptCorretor = string.IsNullOrWhiteSpace(resultado.ScriptCorretor) ? fallback.ScriptCorretor : resultado.ScriptCorretor,
            MensagemCliente = string.IsNullOrWhiteSpace(resultado.MensagemCliente) ? fallback.MensagemCliente : resultado.MensagemCliente,
            MensagemClienteCurta = string.IsNullOrWhiteSpace(resultado.MensagemClienteCurta) ? fallback.MensagemClienteCurta : resultado.MensagemClienteCurta,
            Objecoes = resultado.Objecoes is { Count: > 0 } ? resultado.Objecoes : fallback.Objecoes,
            Alertas = alertas
        };
    }

    private static PlanoDestaque? CompletarDestaque(PlanoDestaque? resultado, PlanoDestaque? fallback)
    {
        if (resultado is null)
        {
            return fallback;
        }

        if (fallback is null)
        {
            return resultado;
        }

        return resultado with
        {
            Operadora = string.IsNullOrWhiteSpace(resultado.Operadora) ? fallback.Operadora : resultado.Operadora,
            Plano = string.IsNullOrWhiteSpace(resultado.Plano) ? fallback.Plano : resultado.Plano,
            Valor = resultado.Valor <= 0 ? fallback.Valor : resultado.Valor,
            Nota = resultado.Nota ?? fallback.Nota,
            Motivo = string.IsNullOrWhiteSpace(resultado.Motivo) ? fallback.Motivo : resultado.Motivo
        };
    }

    private static ItemRanking CriarItemRanking(PlanoNormalizado plano, int posicao)
    {
        var quantidadeRede = QuantidadeRede(plano);
        var nota = Math.Min(10, Math.Round((quantidadeRede / 30m) + (plano.ValorTotal > 0 ? 3 : 0), 2));
        return new ItemRanking(
            posicao,
            plano.Operadora,
            plano.Plano,
            plano.ValorTotal,
            nota,
            plano.Hospitais,
            plano.Clinicas,
            plano.Laboratorios,
            ValorOuNaoInformado(plano.Reembolso),
            ValorOuNaoInformado(plano.Elegibilidade),
            ValorOuNaoInformado(plano.Carencia),
            ValorOuNaoInformado(plano.Coparticipacao),
            ValorOuNaoInformado(plano.CoparticipacaoTerapias),
            plano.DocumentacaoNecessaria,
            plano.AreaComercializacao,
            ValorOuNaoInformado(plano.Odontologia),
            [$"Rede coletada com {quantidadeRede} prestadores classificados.", plano.ValorTotal > 0 ? $"Valor total informado: {plano.ValorTotal:C}." : "Valor total não informado."],
            plano.CamposNaoEncontrados.Count == 0 ? ["Confirmar condições comerciais antes da contratação."] : plano.CamposNaoEncontrados.Select(x => $"{x}: Não informado").ToArray(),
            $"Posição definida pela quantidade de rede coletada e pelo valor disponível, sem uso de dados externos.",
            "Cliente que deseja comparar rede credenciada e custo com transparência.",
            plano.UrlOrigem);
    }

    private static PlanoDestaque? CriarDestaque(PlanoNormalizado? plano, string motivo)
    {
        return plano is null ? null : new PlanoDestaque(plano.Operadora, plano.Plano, plano.ValorTotal, null, motivo);
    }

    private static string CriarScriptComercial(PlanoNormalizado? melhorCusto, PlanoNormalizado? maisEconomico, PlanoNormalizado? melhorRede)
    {
        return $"""
        Abertura: explique que a análise considerou as idades informadas, o CEP e a rede coletada diretamente na simulação.
        Confirmação: valide se o cliente prioriza mensalidade, rede hospitalar, terapias ou menor coparticipação.
        Melhor opção: apresente {NomeValor(melhorCusto)} como melhor equilíbrio encontrado pelos dados disponíveis.
        Alternativa econômica: apresente {NomeValor(maisEconomico)} como opção de menor valor informado.
        Melhor rede: apresente {NomeValor(melhorRede)} como opção com maior rede coletada.
        Pontos de atenção: confirme elegibilidade, carências, coparticipação, terapias e disponibilidade atual da rede antes da contratação.
        Fechamento: Entre essas opções, qual faz mais sentido para você: priorizar uma mensalidade menor ou uma rede mais completa?
        """;
    }

    private static string CriarMensagemCliente(string cep, IReadOnlyList<int> idades, PlanoNormalizado? melhorCusto, PlanoNormalizado? maisEconomico, PlanoNormalizado? melhorRede)
    {
        var opcoes = new[] { melhorCusto, maisEconomico, melhorRede }
            .Where(x => x is not null)
            .DistinctBy(x => $"{x!.Plano}|{x.ValorTotal}")
            .Take(3)
            .Select((x, i) => $"{i + 1}. {x!.Plano ?? "Plano não informado"} - {FormatarValor(x.ValorTotal)}")
            .ToArray();

        return $"""
        Olá! Analisei as opções considerando a(s) idade(s) {string.Join(", ", idades)} e o CEP {cep}.

        Separei estas alternativas:
        {string.Join(Environment.NewLine, opcoes)}

        Antes da contratação, precisamos confirmar elegibilidade, carências, coparticipação e disponibilidade atual da rede.

        O que faz mais sentido para você agora: menor mensalidade ou uma rede mais completa?
        """;
    }

    private static string CriarMensagemCurta(PlanoNormalizado? melhorCusto, PlanoNormalizado? maisEconomico, PlanoNormalizado? melhorRede)
    {
        return $"Opções analisadas: melhor equilíbrio {NomeValor(melhorCusto)}, econômica {NomeValor(maisEconomico)} e maior rede {NomeValor(melhorRede)}. Confirmar rede, carências e elegibilidade antes da contratação.";
    }

    private static IReadOnlyList<ObjecaoResponse> CriarObjecoes()
    {
        return
        [
            new("Qual é o mais barato?", "O mais barato é a opção marcada como mais econômica, considerando apenas o valor informado na simulação."),
            new("Por que o recomendado não é o mais barato?", "Porque custo-benefício considera também a rede coletada e pontos de atenção, não só mensalidade."),
            new("Vale a pena pagar a diferença?", "Depende da prioridade: se rede mais ampla for importante, pode fazer sentido comparar a diferença com os prestadores disponíveis."),
            new("Quais hospitais estão incluídos?", "Os hospitais listados foram coletados da simulação, mas a disponibilidade deve ser confirmada antes da contratação."),
            new("Tem coparticipação?", "Quando a simulação não informou claramente, trate como Não informado e confirme na proposta oficial."),
            new("Como funcionam as terapias?", "As regras de terapias e eventual coparticipação precisam ser confirmadas nas condições comerciais do plano."),
            new("Existe carência?", "Carência deve ser confirmada antes da contratação, especialmente para regras por idade e aproveitamento."),
            new("Posso contratar com essas idades?", "A elegibilidade precisa ser confirmada conforme as regras comerciais da operadora."),
            new("A rede pode mudar?", "Sim, rede credenciada pode sofrer alterações; por isso a confirmação atual é necessária antes da contratação.")
        ];
    }

    private static IReadOnlyList<EstabelecimentoResponse> MapearPrestadores(IReadOnlyList<PrestadorColetado> prestadores, string plano, string urlOrigem)
    {
        return prestadores
            .GroupBy(x => $"{NormalizarTexto(x.Nome)}|{NormalizarTexto(x.Tipo)}|{NormalizarTexto(x.Cidade)}|{NormalizarTexto(x.Bairro)}")
            .Select(x => x.First())
            .Select(x => new EstabelecimentoResponse(
                x.Nome,
                x.Tipo,
                x.Endereco,
                x.Bairro,
                x.Cidade,
                x.Uf,
                null,
                x.Especialidades,
                plano,
                urlOrigem,
                $"{x.Tipo}: {x.Nome}"))
            .ToArray();
    }

    private static IReadOnlyList<PlanoNormalizado> RemoverPlanosDuplicados(IReadOnlyList<PlanoNormalizado> planos)
    {
        return planos
            .GroupBy(x => $"{NormalizarTexto(x.Plano)}|{x.ValorTotal}|{NormalizarTexto(x.Acomodacao)}")
            .Select(x => x.OrderByDescending(QuantidadeRede).First())
            .ToArray();
    }

    private static IReadOnlyList<string> CamposNaoEncontradosFallback(RedePlanoColetada rede)
    {
        var campos = new List<string>
        {
            "operadora",
            "registroAns",
            "tipoContratacao",
            "abrangencia",
            "segmentacao",
            "reembolso",
            "elegibilidade",
            "carencia",
            "coparticipacao",
            "coparticipacaoTerapias",
            "odontologia"
        };

        if (rede.ValorTotal.HasValue)
        {
            campos.Remove("valorTotal");
        }

        return campos;
    }

    private static IReadOnlyList<string> RemoverCamposEncontrados(IReadOnlyList<string> campos, RedePlanoColetada rede)
    {
        var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hospitais", "clinicas", "laboratorios" };
        if (rede.ValorTotal.HasValue)
        {
            encontrados.Add("valorTotal");
        }

        if (!string.IsNullOrWhiteSpace(rede.Acomodacao))
        {
            encontrados.Add("acomodacao");
        }

        return campos.Where(x => !encontrados.Contains(x)).ToArray();
    }

    private static IReadOnlyList<string> Mesclar(IReadOnlyList<string> atual, IReadOnlyList<string> adicionais)
    {
        return atual.Concat(adicionais).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int QuantidadeRede(PlanoNormalizado plano)
    {
        return plano.Hospitais.Count + plano.Clinicas.Count + plano.Laboratorios.Count + plano.CentrosDiagnostico.Count + plano.ProntosSocorros.Count;
    }

    private static int TotalPrestadores(RedePlanoColetada rede)
    {
        return rede.Hospitais.Count + rede.Clinicas.Count + rede.Laboratorios.Count + rede.OutrosPrestadores.Count;
    }

    private static bool NomeCompativel(string? a, string? b)
    {
        var left = NormalizarTexto(a);
        var right = NormalizarTexto(b);
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && (left.Equals(right, StringComparison.OrdinalIgnoreCase)
                || left.Contains(right, StringComparison.OrdinalIgnoreCase)
                || right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValoresCompativeis(decimal valorPlano, decimal? valorRede)
    {
        return valorRede.HasValue && valorPlano > 0 && Math.Abs(valorPlano - valorRede.Value) < 0.01m;
    }

    private static bool ContemTexto(string? value, string text)
    {
        return value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string ValorOuNaoInformado(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Não informado" : value;
    }

    private static string NomeValor(PlanoNormalizado? plano)
    {
        return plano is null ? "não informado" : $"{plano.Plano ?? "Plano não informado"} ({FormatarValor(plano.ValorTotal)})";
    }

    private static string FormatarValor(decimal valor)
    {
        return valor > 0 ? valor.ToString("C") : "valor não informado";
    }

    private static string NormalizarTexto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Trim().ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
    }

    private async Task<EntradaValidada> ValidarAsync(CriarAnaliseRequest request, CancellationToken cancellationToken)
    {
        var cep = Regex.Replace(request.Cep ?? "", "\\D", "");
        if (cep.Length != 8)
        {
            throw new ValidacaoException("CEP_INVALIDO", "CEP deve conter exatamente 8 números.");
        }

        var idades = request.Idades?.ToArray() ?? [];
        if (idades.Length == 0 || idades.Any(x => x < 0 || x > 120))
        {
            throw new ValidacaoException("IDADES_INVALIDAS", "Informe pelo menos uma idade entre 0 e 120.");
        }

        if (idades.Length > 20)
        {
            throw new ValidacaoException("IDADES_INVALIDAS", "O limite é de 20 idades por análise.");
        }

        var links = request.Links?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [];
        if (links.Length == 0)
        {
            throw new ValidacaoException("LINKS_NAO_INFORMADOS", "Informe pelo menos um link.");
        }

        if (links.Length > 10)
        {
            throw new ValidacaoException("LIMITE_LINKS_EXCEDIDO", "O limite é de 10 links por análise.");
        }

        if (links.Distinct(StringComparer.OrdinalIgnoreCase).Count() != links.Length)
        {
            throw new ValidacaoException("LINK_INVALIDO", "Links duplicados não são aceitos.");
        }

        foreach (var link in links)
        {
            await ValidarUrlAsync(link, cancellationToken);
        }

        return new EntradaValidada(cep, idades, links, request.Prioridades?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [], request.Observacoes);
    }

    private static async Task ValidarUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ValidacaoException("LINK_INVALIDO", "Somente URLs HTTPS absolutas são aceitas.", [url]);
        }

        if (!uri.Host.Equals("app.simuladoronline.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidacaoException("DOMINIO_NAO_PERMITIDO", "Somente o domínio app.simuladoronline.com é permitido.", [url]);
        }

        var blockedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1" };
        if (blockedHosts.Contains(uri.Host))
        {
            throw new ValidacaoException("LINK_INVALIDO", "URLs locais não são aceitas.", [url]);
        }

        var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        if (addresses.Any(IsPrivateOrLoopback))
        {
            throw new ValidacaoException("LINK_INVALIDO", "O domínio informado resolve para endereço local ou privado.", [url]);
        }
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 169 && bytes[1] == 254;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.Equals(IPAddress.IPv6Loopback);
    }

    private async Task<Entities.Analise> BuscarAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Analises.Include(x => x.Links).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ValidacaoException("ANALISE_NAO_ENCONTRADA", "Análise não encontrada.");
    }

    private static AnaliseCompletaResponse ToCompleta(Entities.Analise analise)
    {
        return new AnaliseCompletaResponse(
            analise.Id,
            analise.Cep,
            ReadJson<IReadOnlyList<int>>(analise.IdadesJson) ?? [],
            ReadJson<IReadOnlyList<string>>(analise.PrioridadesJson) ?? [],
            analise.Observacoes,
            analise.Status,
            ReadJson<object>(analise.ResultadoJson),
            analise.ResumoCorretor,
            analise.ScriptCorretor,
            analise.MensagemCliente,
            analise.Erro,
            analise.CriadoEm,
            analise.ProcessadoEm,
            analise.Links.Select(link => new AnaliseLinkResponse(
                link.Id,
                link.Url,
                link.Status,
                link.ConteudoPagina,
                ReadJson<object>(link.DadosColetadosJson),
                link.Erro,
                link.CriadoEm,
                link.ProcessadoEm)).ToArray());
    }

    private static T? ReadJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private sealed record EntradaValidada(string Cep, IReadOnlyList<int> Idades, IReadOnlyList<string> Links, IReadOnlyList<string> Prioridades, string? Observacoes);
}
