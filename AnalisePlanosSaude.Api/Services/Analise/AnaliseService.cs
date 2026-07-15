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

                var normalizados = await openRouter.NormalizarColetaAsync(analise.Cep, idades, coleta, cancellationToken);
                planos.AddRange(normalizados);
                link.DadosColetadosJson = JsonSerializer.Serialize(new { coleta.RespostasJson, coleta.ElementosEncontrados, coleta.SecoesColetadas, planos = normalizados }, JsonOptions);
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

        var resultado = await openRouter.CompararPlanosAsync(
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
