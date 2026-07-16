namespace AnalisePlanosSaude.Api.Options;

public sealed class AtualizacaoSimulacoesOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervaloHoras { get; set; } = 24;
    public int HoraExecucao { get; set; } = 3;
    public int MaxAgendamentosPorCiclo { get; set; } = 20;
}
