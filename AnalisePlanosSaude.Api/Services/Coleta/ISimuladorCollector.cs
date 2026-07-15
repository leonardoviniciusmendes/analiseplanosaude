namespace AnalisePlanosSaude.Api.Services.Coleta;

public interface ISimuladorCollector
{
    Task<ColetaLinkResult> ColetarAsync(string url, CancellationToken cancellationToken);
}
