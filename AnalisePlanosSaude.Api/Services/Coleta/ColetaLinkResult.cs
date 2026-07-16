namespace AnalisePlanosSaude.Api.Services.Coleta;

public sealed record ColetaLinkResult(
    string Url,
    string ConteudoPagina,
    IReadOnlyList<RespostaRedeColetada> RespostasJson,
    IReadOnlyList<string> ElementosEncontrados,
    IReadOnlyList<string> SecoesColetadas,
    IReadOnlyList<RedePlanoColetada> RedesPorPlano);

public sealed record RespostaRedeColetada(
    string Url,
    string Metodo,
    int Status,
    string ResourceType,
    string ContentType,
    string Body);

public sealed record RedePlanoColetada(
    string NomePlano,
    string? Acomodacao,
    decimal? ValorTotal,
    int? QuantidadeHospitaisInformada,
    IReadOnlyList<PrestadorColetado> Hospitais,
    IReadOnlyList<PrestadorColetado> Clinicas,
    IReadOnlyList<PrestadorColetado> Laboratorios,
    IReadOnlyList<PrestadorColetado> OutrosPrestadores,
    string? ElementoClicado,
    string? Erro);

public sealed record PrestadorColetado(
    string Nome,
    string Tipo,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Endereco,
    IReadOnlyList<string> Especialidades);
