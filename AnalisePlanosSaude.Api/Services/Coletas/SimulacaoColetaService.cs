using System.Net;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Requests;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Services.Analise;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public sealed class SimulacaoColetaService(AppDbContext db) : ISimulacaoColetaService
{
    private static readonly SimulacaoJobTipo[] JobsIniciais =
    [
        SimulacaoJobTipo.ColetarJsonPrincipal,
        SimulacaoJobTipo.ExtrairPlanosEValores,
        SimulacaoJobTipo.DescobrirEndpointRede,
        SimulacaoJobTipo.ColetarJsonRede,
        SimulacaoJobTipo.ExtrairRedeCredenciada,
        SimulacaoJobTipo.ConsolidarDados
    ];

    public async Task<ColetaSimulacaoResponse> CriarAsync(CriarColetaSimulacaoRequest request, CancellationToken cancellationToken)
    {
        var url = request.Url?.Trim() ?? "";
        var hash = await ValidarEExtrairHashAsync(url, cancellationToken);
        var coleta = new SimulacaoColeta
        {
            UrlOriginal = url,
            HashSimulacao = hash,
            EndpointPrincipal = $"https://app.simuladoronline.com/api/pub/simulador/simulacao/view/{hash}",
            Status = SimulacaoColetaStatus.Criada,
            Jobs = JobsIniciais.Select(x => new SimulacaoJob { Tipo = x }).ToList()
        };

        db.SimulacoesColetas.Add(coleta);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(coleta);
    }

    public async Task<ColetaSimulacaoResponse> ObterAsync(Guid id, CancellationToken cancellationToken)
    {
        var coleta = await BuscarAsync(id, cancellationToken);
        return ToResponse(coleta);
    }

    public async Task<IReadOnlyList<ColetaSimulacaoResponse>> ListarAsync(CancellationToken cancellationToken)
    {
        var coletas = await db.SimulacoesColetas.AsNoTracking()
            .Include(x => x.Jobs)
            .Include(x => x.Planos)
            .ThenInclude(x => x.Prestadores)
            .OrderByDescending(x => x.CriadoEm)
            .Take(100)
            .ToListAsync(cancellationToken);

        return coletas.Select(ToResponse).ToArray();
    }

    public async Task ReagendarJobAsync(Guid id, string tipo, CancellationToken cancellationToken)
    {
        var coleta = await db.SimulacoesColetas.Include(x => x.Jobs).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ValidacaoException("COLETA_NAO_ENCONTRADA", "Coleta não encontrada.");

        if (!Enum.TryParse<SimulacaoJobTipo>(tipo, true, out var tipoJob))
        {
            throw new ValidacaoException("JOB_INVALIDO", "Tipo de job inválido.", [tipo]);
        }

        var job = coleta.Jobs.FirstOrDefault(x => x.Tipo == tipoJob);
        if (job is null)
        {
            job = new SimulacaoJob { SimulacaoColetaId = coleta.Id, Tipo = tipoJob };
            coleta.Jobs.Add(job);
        }

        job.Status = SimulacaoJobStatus.Pendente;
        job.Erro = null;
        job.IniciadoEm = null;
        job.FinalizadoEm = null;
        coleta.Status = SimulacaoColetaStatus.Criada;
        coleta.Erro = null;
        coleta.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SimulacaoColeta> BuscarAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.SimulacoesColetas.AsNoTracking()
            .Include(x => x.Jobs)
            .Include(x => x.Planos)
            .ThenInclude(x => x.Prestadores)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ValidacaoException("COLETA_NAO_ENCONTRADA", "Coleta não encontrada.");
    }

    private static async Task<string> ValidarEExtrairHashAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ValidacaoException("LINK_INVALIDO", "Somente URLs HTTPS absolutas são aceitas.", [url]);
        }

        if (!uri.Host.Equals("app.simuladoronline.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidacaoException("DOMINIO_NAO_PERMITIDO", "Somente o domínio app.simuladoronline.com é permitido.", [url]);
        }

        var hash = uri.Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrWhiteSpace(hash) || hash.Length < 20)
        {
            throw new ValidacaoException("LINK_INVALIDO", "Não foi possível extrair o hash da simulação.", [url]);
        }

        var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        if (addresses.Any(IsPrivateOrLoopback))
        {
            throw new ValidacaoException("LINK_INVALIDO", "O domínio informado resolve para endereço local ou privado.", [url]);
        }

        return hash;
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

    internal static ColetaSimulacaoResponse ToResponse(SimulacaoColeta coleta)
    {
        var planos = coleta.Planos.Select(plano =>
        {
            var hospitais = plano.Prestadores.Count(x => x.Tipo == "Hospital");
            var clinicas = plano.Prestadores.Count(x => x.Tipo == "Clinica");
            var laboratorios = plano.Prestadores.Count(x => x.Tipo == "Laboratorio");
            var outros = plano.Prestadores.Count - hospitais - clinicas - laboratorios;
            return new ColetaPlanoResponse(plano.Id, plano.PlanoIdExterno, plano.Nome, plano.Acomodacao, plano.ValorTotal, hospitais, clinicas, laboratorios, outros);
        }).ToArray();

        return new ColetaSimulacaoResponse(
            coleta.Id,
            coleta.UrlOriginal,
            coleta.HashSimulacao,
            coleta.EndpointPrincipal,
            coleta.EndpointRede,
            coleta.Status,
            coleta.Erro,
            planos.Length,
            coleta.Planos.Sum(x => x.Prestadores.Count),
            coleta.CriadoEm,
            coleta.AtualizadoEm,
            coleta.ProcessadoEm,
            coleta.Jobs.OrderBy(x => Array.IndexOf(JobsIniciais, x.Tipo)).Select(x => new ColetaJobResponse(x.Id, x.Tipo, x.Status, x.Tentativas, x.Erro, x.CriadoEm, x.IniciadoEm, x.FinalizadoEm)).ToArray(),
            planos);
    }
}
