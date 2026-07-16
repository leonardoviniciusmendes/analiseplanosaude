using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Entities;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Options;
using AnalisePlanosSaude.Api.Services.Analise;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public sealed class SimulacaoAtualizacaoService(AppDbContext db, IOptions<AtualizacaoSimulacoesOptions> options) : ISimulacaoAtualizacaoService
{
    private static readonly SimulacaoJobTipo[] PipelineAtualizacao =
    [
        SimulacaoJobTipo.ColetarJsonPrincipal,
        SimulacaoJobTipo.ExtrairPlanosEValores,
        SimulacaoJobTipo.DescobrirEndpointRede,
        SimulacaoJobTipo.ColetarJsonRede,
        SimulacaoJobTipo.ExtrairRedeCredenciada,
        SimulacaoJobTipo.ConsolidarDados
    ];

    private readonly AtualizacaoSimulacoesOptions _options = options.Value;

    public async Task<SimulacaoAtualizacaoJobResponse> AgendarAsync(Guid coletaId, string motivo, CancellationToken cancellationToken)
    {
        var coleta = await db.SimulacoesColetas
            .Include(x => x.Jobs)
            .FirstOrDefaultAsync(x => x.Id == coletaId, cancellationToken)
            ?? throw new ValidacaoException("COLETA_NAO_ENCONTRADA", "Coleta nÃ£o encontrada.");

        var existeAtualizacaoAtiva = await db.SimulacoesAtualizacoesJobs.AnyAsync(x =>
            x.SimulacaoColetaId == coletaId &&
            (x.Status == SimulacaoAtualizacaoJobStatus.Pendente || x.Status == SimulacaoAtualizacaoJobStatus.Executando), cancellationToken);

        if (existeAtualizacaoAtiva)
        {
            throw new ValidacaoException("ATUALIZACAO_EM_ANDAMENTO", "JÃ¡ existe uma atualizaÃ§Ã£o pendente ou em execuÃ§Ã£o para essa simulaÃ§Ã£o.");
        }

        var atualizacao = new SimulacaoAtualizacaoJob
        {
            SimulacaoColetaId = coleta.Id,
            Status = SimulacaoAtualizacaoJobStatus.Executando,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? "Manual" : motivo.Trim(),
            Tentativas = 1,
            IniciadoEm = DateTime.UtcNow
        };

        ReagendarPipeline(coleta);
        db.SimulacoesAtualizacoesJobs.Add(atualizacao);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(atualizacao);
    }

    public async Task<int> AgendarVencidasAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        var limite = DateTime.UtcNow.AddHours(-Math.Max(1, _options.IntervaloHoras));
        var candidatas = await db.SimulacoesColetas
            .Include(x => x.Jobs)
            .Where(x => x.Status == SimulacaoColetaStatus.ColetaConcluida || x.Status == SimulacaoColetaStatus.Concluida)
            .Where(x => !db.SimulacoesAtualizacoesJobs.Any(j =>
                j.SimulacaoColetaId == x.Id &&
                (j.Status == SimulacaoAtualizacaoJobStatus.Pendente || j.Status == SimulacaoAtualizacaoJobStatus.Executando)))
            .Where(x => !db.SimulacoesColetasVersoes.Any(v => v.SimulacaoColetaId == x.Id)
                || db.SimulacoesColetasVersoes.Where(v => v.SimulacaoColetaId == x.Id).Max(v => v.CriadoEm) < limite)
            .OrderBy(x => x.AtualizadoEm)
            .Take(Math.Max(1, _options.MaxAgendamentosPorCiclo))
            .ToListAsync(cancellationToken);

        foreach (var coleta in candidatas)
        {
            var atualizacao = new SimulacaoAtualizacaoJob
            {
                SimulacaoColetaId = coleta.Id,
                Status = SimulacaoAtualizacaoJobStatus.Executando,
                Motivo = "Diaria",
                Tentativas = 1,
                IniciadoEm = DateTime.UtcNow
            };

            ReagendarPipeline(coleta);
            db.SimulacoesAtualizacoesJobs.Add(atualizacao);
        }

        if (candidatas.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return candidatas.Count;
    }

    public async Task<IReadOnlyList<SimulacaoAtualizacaoJobResponse>> ListarJobsAsync(Guid? coletaId, CancellationToken cancellationToken)
    {
        var query = db.SimulacoesAtualizacoesJobs.AsNoTracking().AsQueryable();
        if (coletaId is not null)
        {
            query = query.Where(x => x.SimulacaoColetaId == coletaId);
        }

        var jobs = await query.OrderByDescending(x => x.CriadoEm).Take(200).ToListAsync(cancellationToken);
        return jobs.Select(ToResponse).ToArray();
    }

    public async Task<IReadOnlyList<SimulacaoColetaVersaoResponse>> ListarVersoesAsync(Guid coletaId, CancellationToken cancellationToken)
    {
        var existe = await db.SimulacoesColetas.AnyAsync(x => x.Id == coletaId, cancellationToken);
        if (!existe)
        {
            throw new ValidacaoException("COLETA_NAO_ENCONTRADA", "Coleta nÃ£o encontrada.");
        }

        var versoes = await db.SimulacoesColetasVersoes.AsNoTracking()
            .Include(x => x.Planos)
            .ThenInclude(x => x.Prestadores)
            .Where(x => x.SimulacaoColetaId == coletaId)
            .OrderByDescending(x => x.Versao)
            .ToListAsync(cancellationToken);

        return versoes.Select(x => new SimulacaoColetaVersaoResponse(
            x.Id,
            x.SimulacaoColetaId,
            x.Versao,
            x.Status,
            x.HashConteudo,
            x.DiffJson,
            x.Planos.Count,
            x.Planos.Sum(p => p.Prestadores.Count),
            x.CriadoEm,
            x.ProcessadoEm)).ToArray();
    }

    private static void ReagendarPipeline(SimulacaoColeta coleta)
    {
        foreach (var tipo in PipelineAtualizacao)
        {
            var job = coleta.Jobs.FirstOrDefault(x => x.Tipo == tipo);
            if (job is null)
            {
                coleta.Jobs.Add(new SimulacaoJob
                {
                    SimulacaoColetaId = coleta.Id,
                    Tipo = tipo,
                    Status = SimulacaoJobStatus.Pendente
                });
                continue;
            }

            job.Status = SimulacaoJobStatus.Pendente;
            job.Erro = null;
            job.ResultadoJson = null;
            job.IniciadoEm = null;
            job.FinalizadoEm = null;
        }

        coleta.Status = SimulacaoColetaStatus.Criada;
        coleta.Erro = null;
        coleta.AtualizadoEm = DateTime.UtcNow;
    }

    private static SimulacaoAtualizacaoJobResponse ToResponse(SimulacaoAtualizacaoJob job)
    {
        return new SimulacaoAtualizacaoJobResponse(
            job.Id,
            job.SimulacaoColetaId,
            job.Status,
            job.Motivo,
            job.Tentativas,
            job.VersaoGerada,
            job.DiffJson,
            job.Erro,
            job.CriadoEm,
            job.IniciadoEm,
            job.FinalizadoEm);
    }
}
