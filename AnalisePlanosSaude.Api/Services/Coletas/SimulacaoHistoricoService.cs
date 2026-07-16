using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public sealed class SimulacaoHistoricoService(AppDbContext db) : ISimulacaoHistoricoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SimulacaoColetaVersao> CriarVersaoAsync(SimulacaoColeta coleta, CancellationToken cancellationToken)
    {
        var ultimaVersao = await db.SimulacoesColetasVersoes.AsNoTracking()
            .Include(x => x.Planos).ThenInclude(x => x.ValoresFaixa)
            .Include(x => x.Planos).ThenInclude(x => x.Prestadores)
            .Where(x => x.SimulacaoColetaId == coleta.Id)
            .OrderByDescending(x => x.Versao)
            .FirstOrDefaultAsync(cancellationToken);

        var proximaVersao = (ultimaVersao?.Versao ?? 0) + 1;
        var diff = CriarDiff(ultimaVersao, coleta);
        var diffJson = JsonSerializer.Serialize(diff, JsonOptions);
        var versao = new SimulacaoColetaVersao
        {
            SimulacaoColetaId = coleta.Id,
            Versao = proximaVersao,
            Status = coleta.Status,
            JsonPrincipal = coleta.JsonPrincipal,
            JsonRede = coleta.JsonRede,
            HashConteudo = CalcularHash(coleta),
            DiffJson = diffJson,
            ProcessadoEm = coleta.ProcessadoEm ?? DateTime.UtcNow,
            Planos = coleta.Planos.Select(plano => new SimulacaoPlanoVersao
            {
                PlanoIdExterno = plano.PlanoIdExterno,
                Operadora = plano.Operadora,
                TipoTabela = plano.TipoTabela,
                Nome = plano.Nome,
                Acomodacao = plano.Acomodacao,
                ValorTotal = plano.ValorTotal,
                DadosJson = plano.DadosJson,
                ValoresFaixa = plano.ValoresFaixa.Select(valor => new SimulacaoValorFaixaVersao
                {
                    Faixa = valor.Faixa,
                    IdadeMin = valor.IdadeMin,
                    IdadeMax = valor.IdadeMax,
                    Valor = valor.Valor
                }).ToList(),
                Prestadores = plano.Prestadores.Select(prestador => new SimulacaoPrestadorVersao
                {
                    Tipo = prestador.Tipo,
                    Nome = prestador.Nome,
                    Bairro = prestador.Bairro,
                    Cidade = prestador.Cidade,
                    Uf = prestador.Uf,
                    Endereco = prestador.Endereco,
                    EspecialidadesJson = prestador.EspecialidadesJson,
                    TextoEvidencia = prestador.TextoEvidencia
                }).ToList()
            }).ToList()
        };

        db.SimulacoesColetasVersoes.Add(versao);

        var atualizacao = await db.SimulacoesAtualizacoesJobs
            .Where(x => x.SimulacaoColetaId == coleta.Id && x.Status == SimulacaoAtualizacaoJobStatus.Executando)
            .OrderByDescending(x => x.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        if (atualizacao is not null)
        {
            atualizacao.Status = diff.TemAlteracoes ? SimulacaoAtualizacaoJobStatus.ConcluidoComAlteracoes : SimulacaoAtualizacaoJobStatus.ConcluidoSemMudancas;
            atualizacao.VersaoGerada = proximaVersao;
            atualizacao.DiffJson = diffJson;
            atualizacao.FinalizadoEm = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return versao;
    }

    private static SimulacaoVersaoDiff CriarDiff(SimulacaoColetaVersao? anterior, SimulacaoColeta atual)
    {
        if (anterior is null)
        {
            return new SimulacaoVersaoDiff(
                true,
                atual.Planos.Select(x => x.Nome).Order().ToArray(),
                [],
                [],
                [],
                [],
                [],
                [],
                "Primeira versao historica da simulacao.");
        }

        var anteriores = anterior.Planos.ToDictionary(x => x.PlanoIdExterno, StringComparer.OrdinalIgnoreCase);
        var atuais = atual.Planos.ToDictionary(x => x.PlanoIdExterno, StringComparer.OrdinalIgnoreCase);
        var planosAdicionados = atuais.Keys.Except(anteriores.Keys, StringComparer.OrdinalIgnoreCase).Select(x => atuais[x].Nome).Order().ToArray();
        var planosRemovidos = anteriores.Keys.Except(atuais.Keys, StringComparer.OrdinalIgnoreCase).Select(x => anteriores[x].Nome).Order().ToArray();
        var valoresAlterados = new List<PlanoValorAlteradoDiff>();
        var redeAlterada = new List<PlanoRedeAlteradaDiff>();
        var prestadoresAdicionados = new List<PrestadorAlteradoDiff>();
        var prestadoresRemovidos = new List<PrestadorAlteradoDiff>();

        foreach (var planoAtual in atuais.Values)
        {
            if (!anteriores.TryGetValue(planoAtual.PlanoIdExterno, out var planoAnterior))
            {
                continue;
            }

            if ((planoAnterior.ValorTotal ?? 0) != (planoAtual.ValorTotal ?? 0))
            {
                valoresAlterados.Add(new PlanoValorAlteradoDiff(
                    planoAtual.Nome,
                    planoAnterior.ValorTotal,
                    planoAtual.ValorTotal,
                    CalcularVariacao(planoAnterior.ValorTotal, planoAtual.ValorTotal)));
            }

            var totalAnterior = planoAnterior.Prestadores.Count;
            var totalAtual = planoAtual.Prestadores.Count;
            if (totalAnterior != totalAtual)
            {
                redeAlterada.Add(new PlanoRedeAlteradaDiff(planoAtual.Nome, totalAnterior, totalAtual));
            }

            var redeAnterior = planoAnterior.Prestadores
                .GroupBy(ChavePrestador, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var redeAtual = planoAtual.Prestadores
                .GroupBy(ChavePrestador, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            prestadoresAdicionados.AddRange(redeAtual.Keys
                .Except(redeAnterior.Keys, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(key => ToPrestadorDiff(planoAtual.Nome, redeAtual[key])));

            prestadoresRemovidos.AddRange(redeAnterior.Keys
                .Except(redeAtual.Keys, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(key => ToPrestadorDiff(planoAtual.Nome, redeAnterior[key])));
        }

        var temAlteracoes = planosAdicionados.Length > 0
            || planosRemovidos.Length > 0
            || valoresAlterados.Count > 0
            || redeAlterada.Count > 0
            || prestadoresAdicionados.Count > 0
            || prestadoresRemovidos.Count > 0;

        return new SimulacaoVersaoDiff(
            temAlteracoes,
            planosAdicionados,
            planosRemovidos,
            valoresAlterados,
            redeAlterada,
            prestadoresAdicionados,
            prestadoresRemovidos,
            [],
            temAlteracoes ? "Alteracoes detectadas na atualizacao." : "Nenhuma mudanca relevante detectada.");
    }

    private static string CalcularHash(SimulacaoColeta coleta)
    {
        var payload = JsonSerializer.Serialize(new
        {
            planos = coleta.Planos
                .OrderBy(x => x.PlanoIdExterno)
                .Select(x => new
                {
                    x.PlanoIdExterno,
                    x.Nome,
                    x.ValorTotal,
                    valores = x.ValoresFaixa.OrderBy(v => v.IdadeMin).Select(v => new { v.Faixa, v.Valor }),
                    rede = x.Prestadores.OrderBy(ChavePrestador).Select(p => new { p.Tipo, p.Nome, p.Bairro, p.Cidade, p.Uf })
                })
        }, JsonOptions);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static decimal? CalcularVariacao(decimal? anterior, decimal? atual)
    {
        if (anterior is null || atual is null || anterior == 0)
        {
            return null;
        }

        return Math.Round(((atual.Value - anterior.Value) / anterior.Value) * 100, 2);
    }

    private static string ChavePrestador(SimulacaoPrestador prestador)
    {
        return Normalizar($"{prestador.Tipo}|{prestador.Nome}|{prestador.Bairro}|{prestador.Cidade}|{prestador.Uf}");
    }

    private static string ChavePrestador(SimulacaoPrestadorVersao prestador)
    {
        return Normalizar($"{prestador.Tipo}|{prestador.Nome}|{prestador.Bairro}|{prestador.Cidade}|{prestador.Uf}");
    }

    private static PrestadorAlteradoDiff ToPrestadorDiff(string plano, SimulacaoPrestador prestador)
    {
        return new PrestadorAlteradoDiff(plano, prestador.Tipo, prestador.Nome, prestador.Bairro, prestador.Cidade, prestador.Uf);
    }

    private static PrestadorAlteradoDiff ToPrestadorDiff(string plano, SimulacaoPrestadorVersao prestador)
    {
        return new PrestadorAlteradoDiff(plano, prestador.Tipo, prestador.Nome, prestador.Bairro, prestador.Cidade, prestador.Uf);
    }

    private static string Normalizar(string value)
    {
        var normalized = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private sealed record SimulacaoVersaoDiff(
        bool TemAlteracoes,
        IReadOnlyList<string> PlanosAdicionados,
        IReadOnlyList<string> PlanosRemovidos,
        IReadOnlyList<PlanoValorAlteradoDiff> PlanosComValorAlterado,
        IReadOnlyList<PlanoRedeAlteradaDiff> PlanosComRedeAlterada,
        IReadOnlyList<PrestadorAlteradoDiff> PrestadoresAdicionados,
        IReadOnlyList<PrestadorAlteradoDiff> PrestadoresRemovidos,
        IReadOnlyList<string> Avisos,
        string Resumo);

    private sealed record PlanoValorAlteradoDiff(string Plano, decimal? ValorAnterior, decimal? ValorNovo, decimal? VariacaoPercentual);
    private sealed record PlanoRedeAlteradaDiff(string Plano, int TotalAnterior, int TotalNovo);
    private sealed record PrestadorAlteradoDiff(string Plano, string Tipo, string Nome, string? Bairro, string? Cidade, string? Uf);
}
