namespace AnalisePlanosSaude.Api.Entities;

public enum SimulacaoAtualizacaoJobStatus
{
    Pendente,
    Executando,
    ConcluidoSemMudancas,
    ConcluidoComAlteracoes,
    Erro,
    Cancelado
}
