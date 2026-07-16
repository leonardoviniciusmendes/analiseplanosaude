namespace AnalisePlanosSaude.Api.Entities;

public enum SimulacaoColetaStatus
{
    Criada,
    ColetandoJsonPrincipal,
    ExtraindoPlanosEValores,
    DescobrindoEndpointRede,
    ColetandoJsonRede,
    ExtraindoRedeCredenciada,
    ColetaConcluida,
    ColetaConcluidaComErros,
    AnalisandoIa,
    Concluida,
    Erro
}
