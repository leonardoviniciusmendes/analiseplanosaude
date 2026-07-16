using AnalisePlanosSaude.Api.Services.Analise;

namespace AnalisePlanosSaude.Api.Services.AnalisesComerciais;

public sealed class AnaliseComercialWorker(IServiceScopeFactory scopeFactory, ILogger<AnaliseComercialWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAnaliseComercialService>();
                var processou = await service.ProcessarProximaPendenteAsync(stoppingToken);

                if (!processou)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ValidacaoException ex)
            {
                logger.LogWarning(ex, "Falha de validacao ao processar analise comercial pendente.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha inesperada no worker de analises comerciais.");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
