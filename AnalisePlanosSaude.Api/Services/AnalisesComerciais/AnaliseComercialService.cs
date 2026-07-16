using System.Text.Json;
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

    public async Task<AnaliseComercialResponse> CriarAsync(CriarAnaliseComercialRequest request, CancellationToken cancellationToken)
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

        var analise = new AnaliseComercial
        {
            IdadesJson = JsonSerializer.Serialize(idades, JsonOptions),
            NecessidadesJson = JsonSerializer.Serialize(necessidades, JsonOptions),
            Cep = cep,
            LinkSimulacao = request.LinkSimulacao?.Trim(),
            HashSimulacao = hash,
            FiltrosJson = JsonSerializer.Serialize(new { operadorasPreferidas = operadoras, tipoTabela = request.TipoTabela?.ToString() }, JsonOptions),
            PerfilCliente = request.PerfilCliente,
            PrioridadeVenda = request.PrioridadeVenda,
            ObservacoesCorretor = request.ObservacoesCorretor,
            Status = "Processando"
        };

        db.AnalisesComerciais.Add(analise);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var planos = await BuscarPlanosAsync(hash, operadoras, request.TipoTabela, cancellationToken);
            if (planos.Count == 0)
            {
                throw new ValidacaoException("PLANOS_NAO_ENCONTRADOS", "Nenhum plano salvo foi encontrado para os filtros informados.");
            }

            var dataset = MontarDataset(analise.Id, request, idades, necessidades, cep, hash, operadoras, planos);
            var resultadoBase = CriarResultado(analise.Id, dataset, null, []);
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

            var resultado = CriarResultado(analise.Id, dataset, textosIa, alertas);
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
            return resultado;
        }
        catch (Exception ex)
        {
            analise.Status = "Erro";
            analise.Erro = ex.Message;
            analise.ProcessadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
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

    private static AnaliseComercialResponse CriarResultado(Guid analiseId, AnaliseComercialDataset dataset, AnaliseComercialIaTextos? textosIa, IReadOnlyList<string> alertasExtras)
    {
        var ranking = dataset.Planos.Select((plano, index) => ToRanking(plano, index + 1)).ToArray();
        var melhorCliente = ranking.OrderByDescending(x => x.NotaCliente).ThenBy(x => x.ValorTotal).FirstOrDefault();
        var melhorCorretor = ranking.OrderByDescending(x => x.NotaVenda).ThenBy(x => x.ValorTotal).FirstOrDefault();
        var maisEconomico = ranking.OrderBy(x => x.ValorTotal).FirstOrDefault();
        var melhorRede = ranking.OrderByDescending(x => x.Hospitais).ThenByDescending(x => x.TotalPrestadores).FirstOrDefault();
        var alertas = new List<string>
        {
            "Ranking calculado com valores, idades e rede salvos na base.",
            "Confirme rede atual, carencias, elegibilidade, coparticipacao e disponibilidade antes da contratacao."
        };
        alertas.AddRange(alertasExtras);

        return new AnaliseComercialResponse(
            analiseId,
            "Concluido",
            dataset.Entrada,
            melhorCliente is null ? null : ToDestaque(melhorCliente, textosIa?.MotivoMelhorCliente ?? "Melhor aderencia objetiva ao perfil informado."),
            melhorCorretor is null ? null : ToDestaque(melhorCorretor, textosIa?.MotivoMelhorCorretor ?? "Opcao com melhor chance comercial considerando preco, rede e clareza dos diferenciais."),
            maisEconomico is null ? null : ToDestaque(maisEconomico, "Menor valor total para as idades informadas."),
            melhorRede is null ? null : ToDestaque(melhorRede, "Maior rede hospitalar nos dados salvos."),
            ranking,
            textosIa?.AnaliseCorretor ?? CriarAnaliseCorretor(dataset.Entrada, melhorCliente, melhorCorretor),
            textosIa?.MensagensCliente ?? CriarMensagens(dataset.Entrada, melhorCliente, melhorCorretor, maisEconomico),
            textosIa?.Objecoes is { Count: > 0 } ? textosIa.Objecoes : CriarObjecoes(),
            alertas.Distinct().ToArray());
    }

    private static ItemRankingComercial ToRanking(PlanoComercialDataset plano, int posicao)
    {
        return new ItemRankingComercial(
            posicao,
            plano.PlanoId,
            plano.PlanoIdExterno,
            plano.Nome,
            plano.Operadora,
            plano.TipoTabela,
            plano.ValorTotal,
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
            "Ranking objetivo calculado por preco, rede, necessidades informadas e facilidade comercial.");
    }

    private static PlanoComercialDestaque ToDestaque(ItemRankingComercial item, string motivo)
    {
        return new PlanoComercialDestaque(item.Plano, item.Operadora, item.TipoTabela, item.ValorTotal, item.NotaCliente, item.Hospitais, item.Clinicas, item.Laboratorios, item.TotalPrestadores, motivo);
    }

    private static AnaliseCorretorComercial CriarAnaliseCorretor(EntradaAnaliseComercial entrada, ItemRankingComercial? melhorCliente, ItemRankingComercial? melhorCorretor)
    {
        return new AnaliseCorretorComercial(
            $"Analise para idade(s) {string.Join(", ", entrada.Idades)}. Melhor para cliente: {NomeValor(melhorCliente)}. Melhor para venda: {NomeValor(melhorCorretor)}.",
            ["Comece validando a prioridade do cliente.", "Apresente primeiro a opcao de melhor equilibrio.", "Use a opcao mais economica como comparativo, nao como unica recomendacao."],
            ["Nao prometer permanencia da rede.", "Confirmar carencias, elegibilidade e coparticipacao antes do fechamento."],
            ["Hoje pesa mais mensalidade ou rede?", "Existe hospital ou regiao indispensavel?", "O cliente usa consultas ou terapias com frequencia?"],
            "Conduza a conversa comparando no maximo tres opcoes e feche pedindo uma escolha entre economia e rede.");
    }

    private static MensagensComerciais CriarMensagens(EntradaAnaliseComercial entrada, ItemRankingComercial? melhorCliente, ItemRankingComercial? melhorCorretor, ItemRankingComercial? maisEconomico)
    {
        return new MensagensComerciais(
            "Oi! Posso fazer uma comparacao rapida dos planos considerando sua idade/regiao e te mostrar as opcoes com melhor custo-beneficio?",
            $"Analisei as idades {string.Join(", ", entrada.Idades)}. A opcao mais equilibrada e {NomeValor(melhorCliente)}; para venda eu destacaria {NomeValor(melhorCorretor)}; e a mais economica e {NomeValor(maisEconomico)}. Antes de contratar, precisamos confirmar rede, carencias e elegibilidade.",
            "Conseguiu avaliar as opcoes? Posso te ajudar a comparar mensalidade menor contra uma rede mais completa.",
            "Entre essas opcoes, faz mais sentido avancarmos com a alternativa de menor mensalidade ou com a que entrega uma rede mais completa?");
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

    private static bool Contem(string value, string trecho)
    {
        return value.Contains(trecho, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record AnaliseComercialDataset(
    Guid AnaliseId,
    EntradaAnaliseComercial Entrada,
    string? HashSimulacao,
    IReadOnlyList<PlanoComercialDataset> Planos);

public sealed record PlanoComercialDataset(
    Guid PlanoId,
    string PlanoIdExterno,
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
