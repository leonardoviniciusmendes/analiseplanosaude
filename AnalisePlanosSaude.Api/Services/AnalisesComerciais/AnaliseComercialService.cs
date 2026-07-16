using System.Text.Json;
using System.Security.Cryptography;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.Analise;
using AnalisePlanosSaude.Api.Services.OpenRouter;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Services.AnalisesComerciais;

public sealed class AnaliseComercialService(AppDbContext db, IOpenRouterService openRouter) : IAnaliseComercialService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AnaliseComercialCriadaResponse> CriarAsync(CriarAnaliseComercialRequest request, CancellationToken cancellationToken)
    {
        var idades = ValidarIdades(request.Idades);
        var necessidades = NormalizarLista(request.NecessidadesCliente);
        if (necessidades.Count == 0)
        {
            throw new ValidacaoException("REQUISICAO_INVALIDA", "Informe pelo menos uma necessidade do cliente.");
        }

        var cep = NormalizarCep(request.Cep);
        var hash = ExtrairHashOpcional(request.LinkSimulacao);
        var operadoras = NormalizarLista(request.OperadorasPreferidas ?? []);
        var tipoTabela = NormalizarTipoTabelaComercial(request.TipoTabela);
        var token = await GerarTokenUnicoAsync(cancellationToken);

        var analise = new AnaliseComercial
        {
            TokenConsulta = token,
            IdadesJson = JsonSerializer.Serialize(idades, JsonOptions),
            NecessidadesJson = JsonSerializer.Serialize(necessidades, JsonOptions),
            Cep = cep,
            LinkSimulacao = request.LinkSimulacao?.Trim(),
            HashSimulacao = hash,
            FiltrosJson = JsonSerializer.Serialize(new FiltrosAnaliseComercial(operadoras, tipoTabela), JsonOptions),
            PerfilCliente = request.PerfilCliente,
            PrioridadeVenda = request.PrioridadeVenda,
            ObservacoesCorretor = request.ObservacoesCorretor,
            Status = "Pendente"
        };

        db.AnalisesComerciais.Add(analise);
        await db.SaveChangesAsync(cancellationToken);

        return new AnaliseComercialCriadaResponse(
            analise.Id,
            analise.TokenConsulta,
            analise.Status,
            "Analise comercial recebida. Consulte o status pelo token.",
            $"/api/analises-comerciais/{analise.TokenConsulta}/status",
            $"/api/analises-comerciais/{analise.TokenConsulta}/resultado");
    }

    public async Task<AnaliseComercialResponse> ObterAsync(Guid id, CancellationToken cancellationToken)
    {
        var analise = await db.AnalisesComerciais.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ValidacaoException("ANALISE_NAO_ENCONTRADA", "Analise comercial nao encontrada.");

        if (string.IsNullOrWhiteSpace(analise.ResultadoJson))
        {
            throw new ValidacaoException("ANALISE_SEM_RESULTADO", "A analise comercial ainda nao possui resultado salvo.");
        }

        return JsonSerializer.Deserialize<AnaliseComercialResponse>(analise.ResultadoJson, JsonOptions)
            ?? throw new ValidacaoException("RESPOSTA_IA_INVALIDA", "Resultado salvo esta invalido.");
    }

    public async Task<AnaliseComercialStatusResponse> ObterStatusAsync(string tokenConsulta, CancellationToken cancellationToken)
    {
        var analise = await BuscarPorTokenAsync(tokenConsulta, cancellationToken);
        return ToStatusResponse(analise);
    }

    public async Task<AnaliseComercialResponse> ObterResultadoAsync(string tokenConsulta, CancellationToken cancellationToken)
    {
        var analise = await BuscarPorTokenAsync(tokenConsulta, cancellationToken);
        if (!analise.Status.Equals("Concluido", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(analise.ResultadoJson))
        {
            throw new ValidacaoException("ANALISE_EM_PROCESSAMENTO", "A analise comercial ainda nao foi concluida.");
        }

        return JsonSerializer.Deserialize<AnaliseComercialResponse>(analise.ResultadoJson, JsonOptions)
            ?? throw new ValidacaoException("RESPOSTA_IA_INVALIDA", "Resultado salvo esta invalido.");
    }

    public async Task<bool> ProcessarProximaPendenteAsync(CancellationToken cancellationToken)
    {
        var analise = await db.AnalisesComerciais
            .Where(x => x.Status == "Pendente")
            .OrderBy(x => x.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        if (analise is null)
        {
            return false;
        }

        analise.Status = "Processando";
        analise.Erro = null;
        await db.SaveChangesAsync(cancellationToken);

        await ProcessarAsync(analise.Id, cancellationToken);
        return true;
    }

    private async Task ProcessarAsync(Guid id, CancellationToken cancellationToken)
    {
        var analise = await db.AnalisesComerciais.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ValidacaoException("ANALISE_NAO_ENCONTRADA", "Analise comercial nao encontrada.");

        try
        {
            var idades = JsonSerializer.Deserialize<IReadOnlyList<int>>(analise.IdadesJson, JsonOptions) ?? [];
            var necessidades = JsonSerializer.Deserialize<IReadOnlyList<string>>(analise.NecessidadesJson, JsonOptions) ?? [];
            var filtros = JsonSerializer.Deserialize<FiltrosAnaliseComercial>(analise.FiltrosJson, JsonOptions) ?? new FiltrosAnaliseComercial([], null);
            var operadoras = filtros.OperadorasPreferidas ?? [];
            var tipoTabela = NormalizarTipoTabelaComercial(filtros.TipoTabela);

            var request = new CriarAnaliseComercialRequest(
                idades,
                necessidades,
                analise.PerfilCliente,
                analise.PrioridadeVenda,
                analise.Cep,
                analise.LinkSimulacao,
                operadoras,
                tipoTabela,
                analise.ObservacoesCorretor);

            var planos = await BuscarPlanosAsync(analise.HashSimulacao, operadoras, tipoTabela, cancellationToken);
            if (planos.Count == 0)
            {
                throw new ValidacaoException("PLANOS_NAO_ENCONTRADOS", "Nenhum plano salvo foi encontrado para os filtros informados.");
            }

            var dataset = MontarDataset(analise.Id, request, idades, necessidades, analise.Cep, analise.HashSimulacao, operadoras, planos);
            var resultadoBase = CriarResultado(analise.Id, analise.TokenConsulta, dataset, null, []);
            AnaliseComercialIaTextos? textosIa = null;
            var alertas = new List<string>();

            try
            {
                textosIa = await openRouter.GerarTextosAnaliseComercialAsync(dataset, resultadoBase, cancellationToken);
            }
            catch (Exception ex)
            {
                alertas.Add($"IA nao utilizada: {ex.Message}");
            }

            var resultado = CriarResultado(analise.Id, analise.TokenConsulta, dataset, textosIa, alertas);
            analise.Status = "Concluido";
            analise.DatasetJson = JsonSerializer.Serialize(dataset, JsonOptions);
            analise.ResultadoJson = JsonSerializer.Serialize(resultado, JsonOptions);
            analise.MelhorPlanoCliente = resultado.MelhorParaCliente?.Plano;
            analise.MelhorPlanoCorretor = resultado.MelhorParaCorretorVender?.Plano;
            analise.MensagemCaptacao = resultado.MensagensCliente.CaptacaoInicial;
            analise.MensagemApresentacao = resultado.MensagensCliente.ApresentacaoOpcoes;
            analise.MensagemFechamento = resultado.MensagensCliente.Fechamento;
            analise.ProcessadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            analise.Status = "Erro";
            analise.Erro = ex.Message;
            analise.ProcessadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<IReadOnlyList<SimulacaoPlano>> BuscarPlanosAsync(string? hash, IReadOnlyList<string> operadoras, TipoTabelaPlano? tipoTabela, CancellationToken cancellationToken)
    {
        var query = db.SimulacoesPlanos.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(hash))
        {
            query = query.Where(x => db.SimulacoesColetas.Any(c => c.Id == x.SimulacaoColetaId && c.HashSimulacao == hash));
        }

        if (operadoras.Count > 0)
        {
            query = query.Where(x => x.Operadora != null && operadoras.Contains(x.Operadora));
        }

        if (tipoTabela is not null)
        {
            query = query.Where(x => x.TipoTabela == tipoTabela);
        }

        var planoIds = await query
            .OrderBy(x => x.ValorTotal)
            .Select(x => x.Id)
            .Take(80)
            .ToArrayAsync(cancellationToken);

        return await db.SimulacoesPlanos.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.SimulacaoColeta)
            .Include(x => x.ValoresFaixa)
            .Include(x => x.Prestadores)
            .Where(x => planoIds.Contains(x.Id))
            .OrderBy(x => x.ValorTotal)
            .ToListAsync(cancellationToken);
    }

    private static AnaliseComercialDataset MontarDataset(Guid analiseId, CriarAnaliseComercialRequest request, IReadOnlyList<int> idades, IReadOnlyList<string> necessidades, string? cep, string? hash, IReadOnlyList<string> operadoras, IReadOnlyList<SimulacaoPlano> planos)
    {
        var itens = planos.Select(plano =>
        {
            var valores = idades.Select(idade => ValorParaIdade(plano, idade)).ToArray();
            var valorTotal = valores.Sum(x => x.Valor);
            var hospitais = plano.Prestadores.Where(x => x.Tipo == "Hospital").ToArray();
            var clinicas = plano.Prestadores.Count(x => x.Tipo == "Clinica");
            var laboratorios = plano.Prestadores.Count(x => x.Tipo == "Laboratorio");
            var totalPrestadores = plano.Prestadores.Count;

            return new PlanoComercialDataset(
                plano.Id,
                plano.PlanoIdExterno,
                plano.SimulacaoColeta.UrlOriginal,
                plano.Nome,
                plano.Operadora,
                plano.TipoTabela,
                valorTotal,
                valores.Select(x => new ValorVidaComercialDataset(x.Idade, x.Faixa, x.Valor)).ToArray(),
                hospitais.Length,
                clinicas,
                laboratorios,
                totalPrestadores,
                hospitais.Select(x => x.Nome).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray());
        }).Where(x => x.ValorTotal > 0).ToArray();

        if (itens.Length == 0)
        {
            throw new ValidacaoException("PLANOS_NAO_ENCONTRADOS", "Nenhum plano possui valor para as idades informadas.");
        }

        var menorValor = itens.Min(x => x.ValorTotal);
        var maiorValor = itens.Max(x => x.ValorTotal);
        var maxHospitais = itens.Max(x => x.Hospitais);
        var maxRede = itens.Max(x => x.TotalPrestadores);
        var focoPreco = necessidades.Any(x => Contem(x, "barato") || Contem(x, "mensalidade") || Contem(x, "preco") || Contem(x, "preço"));
        var focoRede = necessidades.Any(x => Contem(x, "hospital") || Contem(x, "rede"));
        var focoTerapia = necessidades.Any(x => Contem(x, "terapia") || Contem(x, "consulta"));

        var pontuados = itens.Select(plano =>
        {
            var notaPreco = maiorValor == menorValor ? 1m : 1 - ((plano.ValorTotal - menorValor) / (maiorValor - menorValor));
            var notaHospitais = maxHospitais == 0 ? 0 : plano.Hospitais / (decimal)maxHospitais;
            var notaRede = maxRede == 0 ? 0 : plano.TotalPrestadores / (decimal)maxRede;
            var notaAderencia = ((focoRede ? notaHospitais : 0.5m) + (focoTerapia ? notaRede : 0.5m) + (focoPreco ? notaPreco : 0.5m)) / 3m;
            var notaCliente = Math.Round((notaPreco * 0.35m + notaHospitais * 0.3m + notaRede * 0.2m + notaAderencia * 0.15m) * 10, 2);
            var notaVenda = Math.Round((notaCliente * 0.55m + notaPreco * 10 * 0.3m + (plano.Hospitais > 0 ? 1.5m : 0m)) , 2);
            var notaCustoBeneficio = Math.Round((notaPreco * 0.45m + notaHospitais * 0.35m + notaRede * 0.2m) * 10, 2);
            return plano with { NotaCliente = notaCliente, NotaVenda = Math.Min(10, notaVenda), NotaCustoBeneficio = notaCustoBeneficio };
        }).OrderByDescending(x => x.NotaCliente).ThenBy(x => x.ValorTotal).ToArray();

        return new AnaliseComercialDataset(
            analiseId,
            new EntradaAnaliseComercial(idades, necessidades, request.PerfilCliente, request.PrioridadeVenda, cep, request.LinkSimulacao, operadoras, request.TipoTabela, request.ObservacoesCorretor),
            hash,
            pontuados);
    }

    private static AnaliseComercialResponse CriarResultado(Guid analiseId, string tokenConsulta, AnaliseComercialDataset dataset, AnaliseComercialIaTextos? textosIa, IReadOnlyList<string> alertasExtras)
    {
        var rankingBase = dataset.Planos.Select((plano, index) => ToRanking(plano, index + 1)).ToArray();
        var melhorClienteBase = rankingBase.OrderByDescending(x => x.NotaCliente).ThenBy(x => x.ValorTotal).FirstOrDefault();
        var maisEconomicoBase = rankingBase.OrderBy(x => x.ValorTotal).FirstOrDefault();
        var melhorRedeBase = rankingBase.OrderByDescending(x => x.Hospitais).ThenByDescending(x => x.TotalPrestadores).FirstOrDefault();
        var maisCaroBase = rankingBase.OrderByDescending(x => x.ValorTotal).FirstOrDefault();
        var custoBeneficioBase = rankingBase.OrderByDescending(x => x.NotaCustoBeneficio).ThenBy(x => x.ValorTotal).FirstOrDefault();
        var intermediarioBase = EscolherIntermediario(rankingBase, maisEconomicoBase, maisCaroBase);
        var melhorCorretorBase = EscolherMelhorParaCorretor(rankingBase, custoBeneficioBase, intermediarioBase, maisEconomicoBase, maisCaroBase);
        var ranking = AplicarEstrategiaRanking(rankingBase, melhorCorretorBase, maisCaroBase, intermediarioBase, custoBeneficioBase, maisEconomicoBase).ToArray();
        var melhorCliente = EncontrarCorrespondente(ranking, melhorClienteBase);
        var melhorCorretor = EncontrarCorrespondente(ranking, melhorCorretorBase);
        var maisEconomico = EncontrarCorrespondente(ranking, maisEconomicoBase);
        var melhorRede = EncontrarCorrespondente(ranking, melhorRedeBase);
        var maisCaro = EncontrarCorrespondente(ranking, maisCaroBase);
        var custoBeneficio = EncontrarCorrespondente(ranking, custoBeneficioBase);
        var intermediario = EncontrarCorrespondente(ranking, intermediarioBase);
        var estrategia = CriarEstrategiaFechamento(textosIa, maisCaro, intermediario, custoBeneficio, maisEconomico);
        var alertas = new List<string>
        {
            "Ranking calculado com valores, idades e rede salvos na base.",
            "A estrategia comercial separa plano premium, intermediario, custo-beneficio e economico para apoiar o fechamento.",
            "Confirme rede atual, carencias, elegibilidade, coparticipacao e disponibilidade antes da contratacao."
        };
        alertas.AddRange(alertasExtras);

        return new AnaliseComercialResponse(
            analiseId,
            tokenConsulta,
            "Concluido",
            dataset.Entrada,
            melhorCliente is null ? null : ToDestaque(melhorCliente, textosIa?.MotivoMelhorCliente ?? "Melhor aderencia objetiva ao perfil informado."),
            melhorCorretor is null ? null : ToDestaque(melhorCorretor, textosIa?.MotivoMelhorCorretor ?? "Opcao principal para conduzir a venda sem depender apenas do maior valor."),
            maisEconomico is null ? null : ToDestaque(maisEconomico, "Menor valor total para as idades informadas."),
            melhorRede is null ? null : ToDestaque(melhorRede, "Maior rede hospitalar nos dados salvos."),
            estrategia,
            ranking,
            textosIa?.AnaliseCorretor ?? CriarAnaliseCorretor(dataset.Entrada, melhorCliente, melhorCorretor, estrategia),
            textosIa?.MensagensCliente ?? CriarMensagens(dataset.Entrada, melhorCliente, melhorCorretor, maisEconomico, estrategia),
            textosIa?.Objecoes is { Count: > 0 } ? textosIa.Objecoes : CriarObjecoes(),
            alertas.Distinct().ToArray());
    }

    private static ItemRankingComercial ToRanking(PlanoComercialDataset plano, int posicao)
    {
        return new ItemRankingComercial(
            posicao,
            plano.PlanoId,
            plano.PlanoIdExterno,
            plano.LinkSimulacao,
            plano.Nome,
            plano.Operadora,
            plano.TipoTabela,
            plano.ValorTotal,
            plano.ValoresPorVida.Select(x => new ValorFaixaEtariaComercial(x.Idade, x.Faixa, x.Valor)).ToArray(),
            plano.NotaCliente,
            plano.NotaVenda,
            plano.NotaCustoBeneficio,
            plano.Hospitais,
            plano.Clinicas,
            plano.Laboratorios,
            plano.TotalPrestadores,
            plano.Hospitais > 0 ? Math.Round(plano.ValorTotal / plano.Hospitais, 2) : 0,
            plano.TotalPrestadores > 0 ? Math.Round(plano.ValorTotal / plano.TotalPrestadores, 2) : 0,
            plano.AmostraHospitais,
            [$"Valor total calculado: {plano.ValorTotal:C}.", $"Rede salva: {plano.Hospitais} hospitais, {plano.Clinicas} clinicas e {plano.Laboratorios} laboratorios."],
            ["Confirmar rede atual antes da contratacao.", "Confirmar carencias, elegibilidade e coparticipacao."],
            "Ranking objetivo calculado por preco, rede, necessidades informadas e facilidade comercial.",
            "Comparativo",
            "Plano mantido no comparativo para apoiar a argumentacao comercial.");
    }

    private static PlanoComercialDestaque ToDestaque(ItemRankingComercial item, string motivo)
    {
        return new PlanoComercialDestaque(item.Plano, item.Operadora, item.TipoTabela, item.ValorTotal, item.NotaCliente, item.Hospitais, item.Clinicas, item.Laboratorios, item.TotalPrestadores, motivo);
    }

    private static ItemRankingComercial? EscolherIntermediario(IReadOnlyList<ItemRankingComercial> ranking, ItemRankingComercial? maisEconomico, ItemRankingComercial? maisCaro)
    {
        var ordenados = ranking.OrderBy(x => x.ValorTotal).ToArray();
        if (ordenados.Length == 0)
        {
            return null;
        }

        if (ordenados.Length <= 2)
        {
            return ordenados.FirstOrDefault(x => !MesmoPlano(x, maisEconomico) && !MesmoPlano(x, maisCaro)) ?? ordenados[0];
        }

        var candidatos = ordenados.Where(x => !MesmoPlano(x, maisEconomico) && !MesmoPlano(x, maisCaro)).ToArray();
        return candidatos.Length == 0 ? ordenados[ordenados.Length / 2] : candidatos[candidatos.Length / 2];
    }

    private static ItemRankingComercial? EscolherMelhorParaCorretor(IReadOnlyList<ItemRankingComercial> ranking, ItemRankingComercial? custoBeneficio, ItemRankingComercial? intermediario, ItemRankingComercial? maisEconomico, ItemRankingComercial? maisCaro)
    {
        if (ranking.Count == 0)
        {
            return null;
        }

        if (custoBeneficio is not null && !MesmoPlano(custoBeneficio, maisCaro))
        {
            return custoBeneficio;
        }

        if (intermediario is not null && !MesmoPlano(intermediario, maisEconomico) && !MesmoPlano(intermediario, maisCaro))
        {
            return intermediario;
        }

        return ranking
            .Where(x => !MesmoPlano(x, maisEconomico) && !MesmoPlano(x, maisCaro))
            .OrderByDescending(x => x.NotaVenda)
            .ThenByDescending(x => x.NotaCustoBeneficio)
            .FirstOrDefault()
            ?? custoBeneficio
            ?? intermediario
            ?? maisEconomico
            ?? maisCaro;
    }

    private static IReadOnlyList<ItemRankingComercial> AplicarEstrategiaRanking(
        IReadOnlyList<ItemRankingComercial> ranking,
        ItemRankingComercial? melhorCorretor,
        ItemRankingComercial? maisCaro,
        ItemRankingComercial? intermediario,
        ItemRankingComercial? custoBeneficio,
        ItemRankingComercial? maisEconomico)
    {
        return ranking.Select(item =>
        {
            var papel = DefinirPapelComercial(item, melhorCorretor, maisCaro, intermediario, custoBeneficio, maisEconomico);
            var motivo = DefinirMotivoNaoEscolhido(item, papel, melhorCorretor, maisCaro, intermediario, custoBeneficio, maisEconomico);
            return item with { PapelComercial = papel, MotivoNaoEscolhidoParaCorretor = motivo };
        }).ToArray();
    }

    private static string DefinirPapelComercial(ItemRankingComercial item, ItemRankingComercial? melhorCorretor, ItemRankingComercial? maisCaro, ItemRankingComercial? intermediario, ItemRankingComercial? custoBeneficio, ItemRankingComercial? maisEconomico)
    {
        if (MesmoPlano(item, melhorCorretor))
        {
            return "OpcaoPrincipalParaCorretor";
        }

        if (MesmoPlano(item, maisCaro))
        {
            return "PremiumMaisCaro";
        }

        if (MesmoPlano(item, intermediario))
        {
            return "Intermediario";
        }

        if (MesmoPlano(item, custoBeneficio))
        {
            return "CustoBeneficio";
        }

        if (MesmoPlano(item, maisEconomico))
        {
            return "MaisBarato";
        }

        return "Comparativo";
    }

    private static string DefinirMotivoNaoEscolhido(ItemRankingComercial item, string papel, ItemRankingComercial? melhorCorretor, ItemRankingComercial? maisCaro, ItemRankingComercial? intermediario, ItemRankingComercial? custoBeneficio, ItemRankingComercial? maisEconomico)
    {
        if (MesmoPlano(item, melhorCorretor))
        {
            return "Foi escolhido como principal para o corretor por equilibrar valor, rede e facilidade de defesa comercial.";
        }

        return papel switch
        {
            "PremiumMaisCaro" => "Nao foi escolhido como principal porque tem o maior ticket; use como ancora premium para mostrar a rede mais completa e valorizar as demais opcoes.",
            "Intermediario" => "Nao foi escolhido como principal porque ficou como alternativa de transicao entre preco e rede; apresente quando o cliente rejeitar o premium.",
            "CustoBeneficio" => "Nao foi escolhido como principal somente se outra opcao teve melhor equilibrio comercial; ainda deve ser usado como argumento central de valor.",
            "MaisBarato" => "Nao foi escolhido como principal porque menor preco pode significar menos rede ou menor forca de argumentacao; use para tratar objecao de mensalidade.",
            _ when item.ValorTotal > (custoBeneficio?.ValorTotal ?? decimal.MaxValue) => "Nao foi escolhido porque custa mais do que a opcao de custo-beneficio sem ganho proporcional claro nos dados salvos.",
            _ when item.Hospitais < (intermediario?.Hospitais ?? 0) => "Nao foi escolhido porque possui rede hospitalar menor do que opcoes mais equilibradas.",
            _ => "Nao foi escolhido como destaque porque outras opcoes apresentaram melhor papel comercial para fechamento."
        };
    }

    private static EstrategiaFechamentoComercial CriarEstrategiaFechamento(AnaliseComercialIaTextos? textosIa, ItemRankingComercial? maisCaro, ItemRankingComercial? intermediario, ItemRankingComercial? custoBeneficio, ItemRankingComercial? maisEconomico)
    {
        var ordem = textosIa?.OrdemApresentacao is { Count: > 0 }
            ? textosIa.OrdemApresentacao
            :
            [
                "Comece pelo premium para ancorar valor e rede.",
                "Mostre o intermediario como caminho mais confortavel.",
                "Defenda o custo-beneficio como melhor equilibrio para fechamento.",
                "Use o mais barato apenas para objecao de preco."
            ];

        return new EstrategiaFechamentoComercial(
            maisCaro is null ? null : ToDestaque(maisCaro, "Maior valor total. Use como ancora premium e comparativo de rede."),
            intermediario is null ? null : ToDestaque(intermediario, "Opcao de meio termo para cliente que quer equilibrio sem ir ao maior ticket."),
            custoBeneficio is null ? null : ToDestaque(custoBeneficio, "Melhor equilibrio objetivo entre preco e rede nos dados salvos."),
            maisEconomico is null ? null : ToDestaque(maisEconomico, "Menor valor total. Use para tratar objecao de mensalidade."),
            textosIa?.EstrategiaUso ?? "Apresente as opcoes em escada: premium para referencia, intermediario para conforto, custo-beneficio para fechamento e mais barato para objecao de preco.",
            ordem);
    }

    private static ItemRankingComercial? EncontrarCorrespondente(IReadOnlyList<ItemRankingComercial> ranking, ItemRankingComercial? item)
    {
        return item is null ? null : ranking.FirstOrDefault(x => MesmoPlano(x, item));
    }

    private static bool MesmoPlano(ItemRankingComercial? left, ItemRankingComercial? right)
    {
        return left is not null && right is not null && left.PlanoId == right.PlanoId;
    }

    private static AnaliseCorretorComercial CriarAnaliseCorretor(EntradaAnaliseComercial entrada, ItemRankingComercial? melhorCliente, ItemRankingComercial? melhorCorretor, EstrategiaFechamentoComercial estrategia)
    {
        return new AnaliseCorretorComercial(
            $"Analise para idade(s) {string.Join(", ", entrada.Idades)}. Melhor para cliente: {NomeValor(melhorCliente)}. Melhor para venda: {NomeValor(melhorCorretor)}.",
            ["Comece validando a prioridade do cliente.", "Use o plano premium como referencia de rede e ticket.", "Apresente o intermediario para reduzir resistencia.", "Defenda o custo-beneficio como opcao natural de fechamento.", "Use a opcao mais economica como comparativo, nao como unica recomendacao."],
            ["Nao prometer permanencia da rede.", "Confirmar carencias, elegibilidade e coparticipacao antes do fechamento."],
            ["Hoje pesa mais mensalidade ou rede?", "Existe hospital ou regiao indispensavel?", "O cliente usa consultas ou terapias com frequencia?"],
            $"{estrategia.EstrategiaUso} Feche pedindo uma escolha entre economia, equilibrio e rede.");
    }

    private static MensagensComerciais CriarMensagens(EntradaAnaliseComercial entrada, ItemRankingComercial? melhorCliente, ItemRankingComercial? melhorCorretor, ItemRankingComercial? maisEconomico, EstrategiaFechamentoComercial estrategia)
    {
        return new MensagensComerciais(
            "Oi! Posso fazer uma comparacao rapida dos planos considerando sua idade/regiao e te mostrar as opcoes com melhor custo-beneficio?",
            $"Analisei as idades {string.Join(", ", entrada.Idades)}. Separei uma opcao premium ({NomeValorFromDestaque(estrategia.MaisCaroPremium)}), uma intermediaria ({NomeValorFromDestaque(estrategia.Intermediario)}), a de melhor custo-beneficio ({NomeValorFromDestaque(estrategia.CustoBeneficio)}) e a mais economica ({NomeValor(maisEconomico)}). Antes de contratar, precisamos confirmar rede, carencias e elegibilidade.",
            "Conseguiu avaliar as opcoes? Posso te ajudar a comparar mensalidade menor contra uma rede mais completa.",
            "Entre essas opcoes, faz mais sentido avancarmos pela mensalidade menor, pelo equilibrio de custo-beneficio ou por uma rede mais completa?");
    }

    private static IReadOnlyList<ObjecaoResponse> CriarObjecoes()
    {
        return
        [
            new("Qual e o mais barato?", "A opcao mais barata esta destacada como menor valor, mas precisamos comparar com rede e uso esperado."),
            new("Por que o recomendado nao e o mais barato?", "Porque a recomendacao considera preco, rede e aderencia ao perfil. O mais barato pode limitar opcoes de atendimento."),
            new("Vale pagar a diferenca?", "Vale quando a diferenca entrega uma rede mais adequada ao uso esperado. Vamos comparar isso de forma objetiva."),
            new("A rede pode mudar?", "Sim. Por isso a confirmacao da rede atual antes da contratacao e obrigatoria.")
        ];
    }

    private static (int Idade, string Faixa, decimal Valor) ValorParaIdade(SimulacaoPlano plano, int idade)
    {
        var faixa = plano.ValoresFaixa
            .OrderBy(x => x.IdadeMin ?? 0)
            .FirstOrDefault(x => (x.IdadeMin is null || idade >= x.IdadeMin) && (x.IdadeMax is null || idade <= x.IdadeMax));

        return faixa is null ? (idade, "Faixa nao encontrada", plano.ValorTotal ?? 0) : (idade, faixa.Faixa, faixa.Valor);
    }

    private static IReadOnlyList<int> ValidarIdades(IReadOnlyList<int>? idades)
    {
        var result = idades?.ToArray() ?? [];
        if (result.Length == 0 || result.Length > 20 || result.Any(x => x < 0 || x > 120))
        {
            throw new ValidacaoException("IDADES_INVALIDAS", "Informe de 1 a 20 idades entre 0 e 120.");
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizarLista(IReadOnlyList<string> values)
    {
        return values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? NormalizarCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep))
        {
            return null;
        }

        var digits = new string(cep.Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
        {
            throw new ValidacaoException("CEP_INVALIDO", "CEP deve conter exatamente 8 numeros.");
        }

        return digits;
    }

    private static TipoTabelaPlano? NormalizarTipoTabelaComercial(TipoTabelaPlano? tipoTabela)
    {
        return tipoTabela is null or TipoTabelaPlano.NaoInformado ? TipoTabelaPlano.Adesao : tipoTabela;
    }

    private static string? ExtrairHashOpcional(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("app.simuladoronline.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidacaoException("LINK_INVALIDO", "Informe um link HTTPS valido do dominio app.simuladoronline.com.");
        }

        var hash = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(hash) || hash.Length < 20
            ? throw new ValidacaoException("LINK_INVALIDO", "Nao foi possivel extrair o hash da simulacao.")
            : hash;
    }

    private static string NomeValor(ItemRankingComercial? item)
    {
        return item is null ? "nao informado" : $"{item.Plano} ({item.ValorTotal:C})";
    }

    private static string NomeValorFromDestaque(PlanoComercialDestaque? item)
    {
        return item is null ? "nao informado" : $"{item.Plano} ({item.ValorTotal:C})";
    }

    private async Task<AnaliseComercial> BuscarPorTokenAsync(string tokenConsulta, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenConsulta))
        {
            throw new ValidacaoException("REQUISICAO_INVALIDA", "Informe o token da analise comercial.");
        }

        return await db.AnalisesComerciais.AsNoTracking().FirstOrDefaultAsync(x => x.TokenConsulta == tokenConsulta.Trim(), cancellationToken)
            ?? throw new ValidacaoException("ANALISE_NAO_ENCONTRADA", "Analise comercial nao encontrada.");
    }

    private static AnaliseComercialStatusResponse ToStatusResponse(AnaliseComercial analise)
    {
        var concluida = analise.Status.Equals("Concluido", StringComparison.OrdinalIgnoreCase);
        return new AnaliseComercialStatusResponse(
            analise.Id,
            analise.TokenConsulta,
            analise.Status,
            concluida,
            analise.Status.Equals("Erro", StringComparison.OrdinalIgnoreCase),
            analise.Erro,
            analise.CriadoEm,
            analise.ProcessadoEm,
            concluida ? $"/api/analises-comerciais/{analise.TokenConsulta}/resultado" : null);
    }

    private async Task<string> GerarTokenUnicoAsync(CancellationToken cancellationToken)
    {
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            var token = GerarToken();
            var existe = await db.AnalisesComerciais.AnyAsync(x => x.TokenConsulta == token, cancellationToken);
            if (!existe)
            {
                return token;
            }
        }

        throw new ValidacaoException("ERRO_INTERNO", "Nao foi possivel gerar token unico para a analise.");
    }

    private static string GerarToken()
    {
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool Contem(string value, string trecho)
    {
        return value.Contains(trecho, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record FiltrosAnaliseComercial(
    IReadOnlyList<string> OperadorasPreferidas,
    TipoTabelaPlano? TipoTabela);

public sealed record AnaliseComercialDataset(
    Guid AnaliseId,
    EntradaAnaliseComercial Entrada,
    string? HashSimulacao,
    IReadOnlyList<PlanoComercialDataset> Planos);

public sealed record PlanoComercialDataset(
    Guid PlanoId,
    string PlanoIdExterno,
    string? LinkSimulacao,
    string Nome,
    string? Operadora,
    TipoTabelaPlano TipoTabela,
    decimal ValorTotal,
    IReadOnlyList<ValorVidaComercialDataset> ValoresPorVida,
    int Hospitais,
    int Clinicas,
    int Laboratorios,
    int TotalPrestadores,
    IReadOnlyList<string> AmostraHospitais)
{
    public decimal NotaCliente { get; init; }
    public decimal NotaVenda { get; init; }
    public decimal NotaCustoBeneficio { get; init; }
}

public sealed record ValorVidaComercialDataset(int Idade, string Faixa, decimal Valor);
