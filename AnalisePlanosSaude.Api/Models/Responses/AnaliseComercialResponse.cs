using AnalisePlanosSaude.Api.Entities;

namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record AnaliseComercialResponse(
    Guid AnaliseId,
    string TokenConsulta,
    string Status,
    EntradaAnaliseComercial Entrada,
    PlanoComercialDestaque? MelhorParaCliente,
    PlanoComercialDestaque? MelhorParaCorretorVender,
    PlanoComercialDestaque? MaisEconomico,
    PlanoComercialDestaque? MelhorRede,
    EstrategiaFechamentoComercial EstrategiaFechamento,
    IReadOnlyList<ItemRankingComercial> Ranking,
    AnaliseCorretorComercial AnaliseCorretor,
    MensagensComerciais MensagensCliente,
    IReadOnlyList<ObjecaoResponse> Objecoes,
    IReadOnlyList<string> Alertas);

public sealed record AnaliseComercialCriadaResponse(
    Guid AnaliseId,
    string TokenConsulta,
    string Status,
    string Mensagem,
    string StatusUrl,
    string ResultadoUrl);

public sealed record AnaliseComercialStatusResponse(
    Guid AnaliseId,
    string TokenConsulta,
    string Status,
    bool Concluida,
    bool PossuiErro,
    string? Erro,
    DateTime CriadoEm,
    DateTime? ProcessadoEm,
    string? ResultadoUrl);

public sealed record EntradaAnaliseComercial(
    IReadOnlyList<int> Idades,
    IReadOnlyList<string> NecessidadesCliente,
    string? PerfilCliente,
    string? PrioridadeVenda,
    string? Cep,
    string? LinkSimulacao,
    IReadOnlyList<string> OperadorasPreferidas,
    TipoTabelaPlano? TipoTabela,
    string? ObservacoesCorretor);

public sealed record PlanoComercialDestaque(
    string Plano,
    string? Operadora,
    TipoTabelaPlano TipoTabela,
    decimal ValorTotal,
    decimal Nota,
    int Hospitais,
    int Clinicas,
    int Laboratorios,
    int TotalPrestadores,
    string Motivo);

public sealed record EstrategiaFechamentoComercial(
    PlanoComercialDestaque? MaisCaroPremium,
    PlanoComercialDestaque? Intermediario,
    PlanoComercialDestaque? CustoBeneficio,
    PlanoComercialDestaque? MaisBarato,
    string EstrategiaUso,
    IReadOnlyList<string> OrdemApresentacao);

public sealed record ItemRankingComercial(
    int Posicao,
    Guid PlanoId,
    string PlanoIdExterno,
    string? LinkSimulacao,
    string Plano,
    string? Operadora,
    TipoTabelaPlano TipoTabela,
    decimal ValorTotal,
    IReadOnlyList<ValorFaixaEtariaComercial> ValoresPorFaixaEtaria,
    decimal NotaCliente,
    decimal NotaVenda,
    decimal NotaCustoBeneficio,
    int Hospitais,
    int Clinicas,
    int Laboratorios,
    int TotalPrestadores,
    decimal CustoPorHospital,
    decimal CustoPorPrestador,
    IReadOnlyList<string> AmostraHospitais,
    IReadOnlyList<string> Vantagens,
    IReadOnlyList<string> PontosAtencao,
    string MotivoPosicao,
    string PapelComercial,
    string MotivoNaoEscolhidoParaCorretor);

public sealed record ValorFaixaEtariaComercial(
    int Idade,
    string Faixa,
    decimal Valor);

public sealed record AnaliseCorretorComercial(
    string ResumoEstrategico,
    IReadOnlyList<string> ArgumentosDeVenda,
    IReadOnlyList<string> PontosDeAtencao,
    IReadOnlyList<string> PerguntasParaQualificar,
    string ComoConduzirConversa);

public sealed record MensagensComerciais(
    string CaptacaoInicial,
    string ApresentacaoOpcoes,
    string FollowUp,
    string Fechamento);

public sealed record AnaliseComercialIaTextos(
    AnaliseCorretorComercial? AnaliseCorretor,
    MensagensComerciais? MensagensCliente,
    IReadOnlyList<ObjecaoResponse>? Objecoes,
    string? MotivoMelhorCliente,
    string? MotivoMelhorCorretor,
    string? EstrategiaUso,
    IReadOnlyList<string>? OrdemApresentacao);
