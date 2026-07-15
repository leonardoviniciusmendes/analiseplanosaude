namespace AnalisePlanosSaude.Api.Services.Coleta;

public sealed record ColetaLinkResult(
    string Url,
    string ConteudoPagina,
    IReadOnlyList<RespostaRedeColetada> RespostasJson,
    IReadOnlyList<string> ElementosEncontrados,
    IReadOnlyList<string> SecoesColetadas);

public sealed record RespostaRedeColetada(
    string Url,
    string Metodo,
    int Status,
    string ResourceType,
    string ContentType,
    string Body);
