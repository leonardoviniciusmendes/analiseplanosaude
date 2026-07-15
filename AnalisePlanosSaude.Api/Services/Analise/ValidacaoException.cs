namespace AnalisePlanosSaude.Api.Services.Analise;

public sealed class ValidacaoException(string codigo, string mensagem, IReadOnlyList<string>? detalhes = null) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
    public IReadOnlyList<string> Detalhes { get; } = detalhes ?? [];
}
