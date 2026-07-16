using AnalisePlanosSaude.Api.Options;
using Microsoft.Extensions.Options;

namespace AnalisePlanosSaude.Api.Services.OpenRouter;

public sealed class OpenRouterModelosWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OpenRouterModelosOptions> options,
    ILogger<OpenRouterModelosWorker> logger) : BackgroundService
{
    private readonly OpenRouterModelosOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IOpenRouterModelosService>();
                var result = await service.SincronizarAsync(stoppingToken);
                logger.LogInformation("Modelos OpenRouter sincronizados: {Modelos} recebidos, {Criados} criados, {Atualizados} atualizados.", result.ModelosRecebidos, result.ModelosCriados, result.ModelosAtualizados);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao sincronizar modelos OpenRouter.");
            }

            await Task.Delay(ProximoIntervalo(), stoppingToken);
        }
    }

    private TimeSpan ProximoIntervalo()
    {
        var intervaloHoras = Math.Max(1, _options.IntervaloHoras);
        var agora = DateTime.Now;
        var proximaHora = new DateTime(agora.Year, agora.Month, agora.Day, Math.Clamp(_options.HoraExecucao, 0, 23), 0, 0);
        if (proximaHora <= agora)
        {
            proximaHora = proximaHora.AddHours(intervaloHoras);
        }

        return proximaHora - agora;
    }
}
