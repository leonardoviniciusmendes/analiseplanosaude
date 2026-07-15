namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record ResultadoAnaliseResponse(
    Guid AnaliseId,
    string Status,
    EntradaResultado Entrada,
    ProcessamentoResultado Processamento,
    PlanoDestaque? MelhorCustoBeneficio,
    PlanoDestaque? MaisEconomico,
    PlanoDestaque? MelhorRede,
    PlanoDestaque? MelhorParaTerapias,
    IReadOnlyList<ItemRanking> Ranking,
    string ResumoCorretor,
    string ScriptCorretor,
    string MensagemCliente,
    string MensagemClienteCurta,
    IReadOnlyList<ObjecaoResponse> Objecoes,
    IReadOnlyList<string> Alertas);

public sealed record EntradaResultado(string Cep, IReadOnlyList<int> Idades, int QuantidadeLinks, IReadOnlyList<string> Prioridades, string? Observacoes);
public sealed record ProcessamentoResultado(int LinksProcessados, int LinksComSucesso, int LinksComErro, int QuantidadePlanos, IReadOnlyList<string> Avisos);
public sealed record PlanoDestaque(string? Operadora, string? Plano, decimal Valor, decimal? Nota, string Motivo);
public sealed record ObjecaoResponse(string Pergunta, string RespostaSugerida);

public sealed record ItemRanking(
    int Posicao,
    string? Operadora,
    string? Plano,
    decimal Valor,
    decimal NotaCustoBeneficio,
    IReadOnlyList<EstabelecimentoResponse> Hospitais,
    IReadOnlyList<EstabelecimentoResponse> Clinicas,
    IReadOnlyList<EstabelecimentoResponse> Laboratorios,
    string? Reembolso,
    string? Elegibilidade,
    string? Carencia,
    string? Coparticipacao,
    string? CoparticipacaoTerapias,
    IReadOnlyList<string> DocumentacaoNecessaria,
    IReadOnlyList<string> AreaComercializacao,
    string? Odontologia,
    IReadOnlyList<string> Vantagens,
    IReadOnlyList<string> PontosAtencao,
    string MotivoPosicao,
    string IndicadoPara,
    string UrlOrigem);

public sealed record EstabelecimentoResponse(
    string? Nome,
    string? Tipo,
    string? Endereco,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Cep,
    IReadOnlyList<string> EspecialidadesOuServicos,
    string? Plano,
    string? LinkOrigem,
    string? TextoEvidencia);
