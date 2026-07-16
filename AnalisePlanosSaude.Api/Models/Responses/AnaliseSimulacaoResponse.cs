namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record AnaliseSimulacaoResponse(
    Guid AnaliseId,
    Guid SimulacaoColetaId,
    string Status,
    string Link,
    IReadOnlyList<int> Idades,
    IReadOnlyList<string> FaixasUtilizadas,
    IReadOnlyList<string> Prioridades,
    string? Observacoes,
    PlanoVendaDestaque? MelhorOpcaoVenda,
    PlanoVendaDestaque? MaisBarato,
    PlanoVendaDestaque? MelhorRedeHospitalar,
    IReadOnlyList<ItemRankingVenda> Ranking,
    string ResumoCorretor,
    string ScriptCorretor,
    string MensagemWhatsApp,
    string MensagemWhatsAppCurta,
    IReadOnlyList<ObjecaoResponse> Objecoes,
    IReadOnlyList<string> Alertas);

public sealed record PlanoVendaDestaque(
    string Plano,
    decimal Valor,
    decimal Nota,
    int Hospitais,
    int Clinicas,
    int Laboratorios,
    int TotalPrestadores,
    string Motivo);

public sealed record ItemRankingVenda(
    int Posicao,
    string Plano,
    decimal Valor,
    decimal NotaVenda,
    decimal CustoPorHospital,
    decimal CustoPorPrestador,
    int Hospitais,
    int Clinicas,
    int Laboratorios,
    int TotalPrestadores,
    IReadOnlyList<string> AmostraHospitais,
    IReadOnlyList<string> Vantagens,
    IReadOnlyList<string> PontosAtencao,
    string MotivoPosicao);
