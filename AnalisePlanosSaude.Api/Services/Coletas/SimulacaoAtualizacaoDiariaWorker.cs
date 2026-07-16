using AnalisePlanosSaude.Api.Options;
using Microsoft.Extensions.Options;

namespace AnalisePlanosSaude.Api.Services.Coletas;

public sealed class SimulacaoAtualizacaoDiariaWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AtualizacaoSimulacoesOptions> options,
    ILogger<SimulacaoAtualizacaoDiariaWorker> logger) : BackgroundService
{
    private readonly AtualizacaoSimulacoesOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Enabled && DeveExecutarAgora())
                {
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ISimulacaoAtualizacaoService>();
                    var agendadas = await service.AgendarVencidasAsync(stoppingToken);
                    if (agendadas > 0)
                    {
                        logger.LogInformation("Atualizacao diaria agendou {Quantidade} simulacoes.", agendadas);
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao agendar atualizacoes diarias de simulacoes.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private bool DeveExecutarAgora()
    {
        var agora = DateTime.Now;
        var hora = Math.Clamp(_options.HoraExecucao, 0, 23);
        return agora.Hour == hora;
    }
}
