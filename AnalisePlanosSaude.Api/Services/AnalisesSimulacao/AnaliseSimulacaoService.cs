using System.Text.Json;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.Analise;
using AnalisePlanosSaude.Api.Services.OpenRouter;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Services.AnalisesSimulacao;

public sealed class AnaliseSimulacaoService(AppDbContext db, IOpenRouterService openRouter) : IAnaliseSimulacaoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AnaliseSimulacaoResponse> CriarAsync(CriarAnaliseSimulacaoRequest request, CancellationToken cancellationToken)
    {
        var link = request.Link?.Trim() ?? "";
        var hash = ExtrairHash(link);
        var idades = ValidarIdades(request.Idades);
        var prioridades = request.Prioridades?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

        var coleta = await db.SimulacoesColetas
            .Include(x => x.Planos)
            .ThenInclude(x => x.ValoresFaixa)
            .Include(x => x.Planos)
            .ThenInclude(x => x.Prestadores)
            .Where(x => x.HashSimulacao == hash)
            .OrderByDescending(x => x.ProcessadoEm ?? x.AtualizadoEm)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidacaoException("COLETA_NAO_ENCONTRADA", "Ainda não existe coleta para essa simulação. Execute POST /api/coletas primeiro.", [link]);

        if (coleta.Status != SimulacaoColetaStatus.ColetaConcluida && coleta.Status != SimulacaoColetaStatus.Concluida)
        {
            throw new ValidacaoException("COLETA_NAO_CONCLUIDA", "A coleta da simulação ainda não foi concluída.", [coleta.Status.ToString(), coleta.Id.ToString()]);
        }

        var analise = new SimulacaoAnalise
        {
            SimulacaoColetaId = coleta.Id,
            LinkOriginal = link,
            HashSimulacao = hash,
            IdadesJson = JsonSerializer.Serialize(idades, JsonOptions),
            PrioridadesJson = JsonSerializer.Serialize(prioridades, JsonOptions),
            Observacoes = request.Observacoes,
            Status = "Processando"
        };

        db.SimulacoesAnalises.Add(analise);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var dataset = MontarDataset(coleta, link, idades, prioridades, request.Observacoes);
            analise.FaixasUtilizadasJson = JsonSerializer.Serialize(dataset.FaixasUtilizadas, JsonOptions);
            analise.DatasetJson = JsonSerializer.Serialize(dataset, JsonOptions);

            var resultadoBase = CriarResultado(analise.Id, coleta.Id, link, idades, prioridades, request.Observacoes, dataset, null);
            AnaliseSimulacaoIaTextos? textosIa = null;
            var alertas = new List<string>();

            try
            {
                textosIa = await openRouter.GerarTextosAnaliseSimulacaoAsync(dataset, resultadoBase, cancellationToken);
            }
            catch (Exception ex)
            {
                alertas.Add($"IA não utilizada: {ex.Message}");
            }

            var resultado = CriarResultado(analise.Id, coleta.Id, link, idades, prioridades, request.Observacoes, dataset, textosIa, alertas);
            analise.Status = "Concluido";
            analise.ResultadoJson = JsonSerializer.Serialize(resultado, JsonOptions);
            analise.ResumoCorretor = resultado.ResumoCorretor;
            analise.ScriptCorretor = resultado.ScriptCorretor;
            analise.MensagemWhatsApp = resultado.MensagemWhatsApp;
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

    public async Task<AnaliseSimulacaoResponse> ObterAsync(Guid id, CancellationToken cancellationToken)
    {
        var analise = await db.SimulacoesAnalises.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ValidacaoException("ANALISE_NAO_ENCONTRADA", "Análise de simulação não encontrada.");

        if (string.IsNullOrWhiteSpace(analise.ResultadoJson))
        {
            throw new ValidacaoException("ANALISE_SEM_RESULTADO", "A análise ainda não possui resultado salvo.");
        }

        return JsonSerializer.Deserialize<AnaliseSimulacaoResponse>(analise.ResultadoJson, JsonOptions)
            ?? throw new ValidacaoException("RESPOSTA_IA_INVALIDA", "Resultado salvo está inválido.");
    }

    private static AnaliseSimulacaoDataset MontarDataset(SimulacaoColeta coleta, string link, IReadOnlyList<int> idades, IReadOnlyList<string> prioridades, string? observacoes)
    {
        var planos = coleta.Planos.Select(plano =>
        {
            var valores = idades.Select(idade => ValorParaIdade(plano, idade)).ToArray();
            var valorTotal = valores.Sum(x => x.Valor);
            var hospitais = plano.Prestadores.Where(x => x.Tipo == "Hospital").ToArray();
            var clinicas = plano.Prestadores.Count(x => x.Tipo == "Clinica");
            var laboratorios = plano.Prestadores.Count(x => x.Tipo == "Laboratorio");
            var totalPrestadores = plano.Prestadores.Count;

            return new PlanoVendaDataset(
                plano.Id,
                plano.PlanoIdExterno,
                plano.Nome,
                valorTotal,
                valores.Select(x => new ValorVidaDataset(x.Idade, x.Faixa, x.Valor)).ToArray(),
                hospitais.Length,
                clinicas,
                laboratorios,
                totalPrestadores,
                hospitais.Select(x => x.Nome).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray());
        }).ToArray();

        var menorValor = planos.Where(x => x.Valor > 0).Min(x => (decimal?)x.Valor) ?? 0;
        var maiorValor = planos.Max(x => x.Valor);
        var maxHospitais = planos.Max(x => x.Hospitais);
        var maxRede = planos.Max(x => x.TotalPrestadores);
        var pesoPreco = prioridades.Any(x => Contem(x, "preço") || Contem(x, "preco") || Contem(x, "barato") || Contem(x, "mensalidade")) ? 0.55m : 0.4m;
        var pesoHospitais = prioridades.Any(x => Contem(x, "hospital")) ? 0.45m : 0.35m;
        var pesoRede = 1m - pesoPreco - pesoHospitais;
        if (pesoRede < 0.1m)
        {
            pesoRede = 0.1m;
            pesoPreco -= 0.05m;
            pesoHospitais -= 0.05m;
        }

        var pontuados = planos.Select(plano =>
        {
            var notaPreco = maiorValor == menorValor || plano.Valor <= 0 ? 0.5m : 1 - ((plano.Valor - menorValor) / (maiorValor - menorValor));
            var notaHospitais = maxHospitais == 0 ? 0 : plano.Hospitais / (decimal)maxHospitais;
            var notaRede = maxRede == 0 ? 0 : plano.TotalPrestadores / (decimal)maxRede;
            var nota = Math.Round((notaPreco * pesoPreco + notaHospitais * pesoHospitais + notaRede * pesoRede) * 10, 2);
            return plano with { Nota = nota };
        }).OrderByDescending(x => x.Nota).ThenBy(x => x.Valor).ToArray();

        return new AnaliseSimulacaoDataset(
            coleta.Id,
            link,
            coleta.HashSimulacao,
            idades,
            pontuados.SelectMany(x => x.ValoresPorVida.Select(v => v.Faixa)).Distinct().ToArray(),
            prioridades,
            observacoes,
            pontuados);
    }

    private static AnaliseSimulacaoResponse CriarResultado(Guid analiseId, Guid coletaId, string link, IReadOnlyList<int> idades, IReadOnlyList<string> prioridades, string? observacoes, AnaliseSimulacaoDataset dataset, AnaliseSimulacaoIaTextos? textosIa, IReadOnlyList<string>? alertasExtras = null)
    {
        var ranking = dataset.Planos.Select((plano, index) => new ItemRankingVenda(
            index + 1,
            plano.Nome,
            plano.Valor,
            plano.Nota,
            plano.Hospitais > 0 ? Math.Round(plano.Valor / plano.Hospitais, 2) : 0,
            plano.TotalPrestadores > 0 ? Math.Round(plano.Valor / plano.TotalPrestadores, 2) : 0,
            plano.Hospitais,
            plano.Clinicas,
            plano.Laboratorios,
            plano.TotalPrestadores,
            plano.AmostraHospitais,
            CriarVantagens(plano),
            CriarPontosAtencao(plano),
            "Ranking calculado com preço por idade, hospitais, rede total e prioridades informadas.")).ToArray();

        var melhor = ranking.FirstOrDefault();
        var maisBarato = ranking.Where(x => x.Valor > 0).OrderBy(x => x.Valor).FirstOrDefault();
        var melhorRede = ranking.OrderByDescending(x => x.Hospitais).ThenByDescending(x => x.TotalPrestadores).FirstOrDefault();
        var alertas = new List<string>
        {
            "Valores calculados pelas faixas etárias salvas na simulação.",
            "Confirme elegibilidade, carências, coparticipação e disponibilidade atual da rede antes da contratação."
        };

        if (alertasExtras is not null)
        {
            alertas.AddRange(alertasExtras);
        }

        return new AnaliseSimulacaoResponse(
            analiseId,
            coletaId,
            "Concluido",
            link,
            idades,
            dataset.FaixasUtilizadas,
            prioridades,
            observacoes,
            melhor is null ? null : ToDestaque(melhor, textosIa?.MotivoMelhorOpcao ?? "Melhor equilíbrio calculado entre preço e rede para as idades informadas."),
            maisBarato is null ? null : ToDestaque(maisBarato, "Menor preço total para as idades informadas."),
            melhorRede is null ? null : ToDestaque(melhorRede, "Maior rede hospitalar encontrada nos dados salvos."),
            ranking,
            textosIa?.ResumoCorretor ?? CriarResumoCorretor(idades, melhor, maisBarato, melhorRede),
            textosIa?.ScriptCorretor ?? CriarScript(melhor, maisBarato, melhorRede),
            textosIa?.MensagemWhatsApp ?? CriarMensagemWhatsApp(idades, melhor, maisBarato, melhorRede),
            textosIa?.MensagemWhatsAppCurta ?? CriarMensagemCurta(melhor, maisBarato, melhorRede),
            textosIa?.Objecoes is { Count: > 0 } ? textosIa.Objecoes : CriarObjecoes(),
            alertas.Distinct().ToArray());
    }

    private static (int Idade, string Faixa, decimal Valor) ValorParaIdade(SimulacaoPlano plano, int idade)
    {
        var faixa = plano.ValoresFaixa
            .OrderBy(x => x.IdadeMin ?? 0)
            .FirstOrDefault(x => (x.IdadeMin is null || idade >= x.IdadeMin) && (x.IdadeMax is null || idade <= x.IdadeMax));

        if (faixa is not null)
        {
            return (idade, faixa.Faixa, faixa.Valor);
        }

        return (idade, "Faixa não encontrada", plano.ValorTotal ?? 0);
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

    private static string ExtrairHash(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("app.simuladoronline.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidacaoException("LINK_INVALIDO", "Informe um link HTTPS válido do domínio app.simuladoronline.com.", [link]);
        }

        var hash = uri.Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrWhiteSpace(hash) || hash.Length < 20)
        {
            throw new ValidacaoException("LINK_INVALIDO", "Não foi possível extrair o hash da simulação.", [link]);
        }

        return hash;
    }

    private static PlanoVendaDestaque ToDestaque(ItemRankingVenda item, string motivo)
    {
        return new PlanoVendaDestaque(item.Plano, item.Valor, item.NotaVenda, item.Hospitais, item.Clinicas, item.Laboratorios, item.TotalPrestadores, motivo);
    }

    private static IReadOnlyList<string> CriarVantagens(PlanoVendaDataset plano)
    {
        return
        [
            $"Valor total para as vidas informadas: {plano.Valor:C}.",
            $"Rede salva: {plano.Hospitais} hospitais, {plano.Clinicas} clínicas e {plano.Laboratorios} laboratórios.",
            $"Nota objetiva de venda: {plano.Nota}."
        ];
    }

    private static IReadOnlyList<string> CriarPontosAtencao(PlanoVendaDataset plano)
    {
        return
        [
            "Confirmar rede credenciada atual antes da contratação.",
            "Confirmar carências, elegibilidade, coparticipação e documentação.",
            plano.Hospitais == 0 ? "Nenhum hospital foi classificado para este plano nos dados salvos." : "Rede hospitalar disponível nos dados coletados."
        ];
    }

    private static string CriarResumoCorretor(IReadOnlyList<int> idades, ItemRankingVenda? melhor, ItemRankingVenda? maisBarato, ItemRankingVenda? melhorRede)
    {
        return $"Análise feita para a(s) idade(s) {string.Join(", ", idades)}. Melhor opção de venda: {NomeValor(melhor)}. Mais barato: {NomeValor(maisBarato)}. Melhor rede hospitalar: {NomeValor(melhorRede)}.";
    }

    private static string CriarScript(ItemRankingVenda? melhor, ItemRankingVenda? maisBarato, ItemRankingVenda? melhorRede)
    {
        return $"""
        Abertura: explique que você analisou os valores pela idade informada e a rede salva da simulação.
        Necessidade: confirme se o cliente prefere menor mensalidade, mais hospitais ou equilíbrio entre preço e rede.
        Recomendação: apresente {NomeValor(melhor)} como melhor equilíbrio.
        Alternativa econômica: apresente {NomeValor(maisBarato)}.
        Melhor rede: apresente {NomeValor(melhorRede)}.
        Transparência: reforce que rede, carências, elegibilidade e coparticipação precisam ser confirmadas antes da contratação.
        Fechamento: Entre essas opções, faz mais sentido priorizar economia ou uma rede mais completa?
        """;
    }

    private static string CriarMensagemWhatsApp(IReadOnlyList<int> idades, ItemRankingVenda? melhor, ItemRankingVenda? maisBarato, ItemRankingVenda? melhorRede)
    {
        return $"""
        Olá! Analisei as opções considerando a(s) idade(s) {string.Join(", ", idades)}.

        Separei três caminhos:
        1. Melhor equilíbrio: {NomeValor(melhor)}
        2. Menor mensalidade: {NomeValor(maisBarato)}
        3. Rede mais completa: {NomeValor(melhorRede)}

        Antes de contratar, precisamos confirmar rede atual, carências, elegibilidade e coparticipação.

        Para você, faz mais sentido economizar na mensalidade ou priorizar uma rede maior?
        """;
    }

    private static string CriarMensagemCurta(ItemRankingVenda? melhor, ItemRankingVenda? maisBarato, ItemRankingVenda? melhorRede)
    {
        return $"Melhor equilíbrio: {NomeValor(melhor)}. Mais econômico: {NomeValor(maisBarato)}. Melhor rede: {NomeValor(melhorRede)}. Confirmar rede e condições antes da contratação.";
    }

    private static IReadOnlyList<ObjecaoResponse> CriarObjecoes()
    {
        return
        [
            new("Qual é o mais barato?", "O plano mais barato é a opção destacada como menor mensalidade para as idades informadas."),
            new("Por que o recomendado não é sempre o mais barato?", "Porque a recomendação considera preço e rede. Às vezes pagar um pouco mais aumenta bastante a rede disponível."),
            new("Quais hospitais estão incluídos?", "A lista vem da rede salva da simulação, mas deve ser confirmada antes da contratação."),
            new("A rede pode mudar?", "Sim. Por isso a confirmação da rede atual é obrigatória antes de fechar.")
        ];
    }

    private static string NomeValor(ItemRankingVenda? item)
    {
        return item is null ? "não informado" : $"{item.Plano} ({item.Valor:C})";
    }

    private static bool Contem(string value, string trecho)
    {
        return value.Contains(trecho, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record AnaliseSimulacaoDataset(
    Guid SimulacaoColetaId,
    string Link,
    string Hash,
    IReadOnlyList<int> Idades,
    IReadOnlyList<string> FaixasUtilizadas,
    IReadOnlyList<string> Prioridades,
    string? Observacoes,
    IReadOnlyList<PlanoVendaDataset> Planos);

public sealed record PlanoVendaDataset(
    Guid PlanoId,
    string PlanoIdExterno,
    string Nome,
    decimal Valor,
    IReadOnlyList<ValorVidaDataset> ValoresPorVida,
    int Hospitais,
    int Clinicas,
    int Laboratorios,
    int TotalPrestadores,
    IReadOnlyList<string> AmostraHospitais)
{
    public decimal Nota { get; init; }
}

public sealed record ValorVidaDataset(int Idade, string Faixa, decimal Valor);

public sealed record AnaliseSimulacaoIaTextos(
    string? ResumoCorretor,
    string? ScriptCorretor,
    string? MensagemWhatsApp,
    string? MensagemWhatsAppCurta,
    string? MotivoMelhorOpcao,
    IReadOnlyList<ObjecaoResponse>? Objecoes);
