namespace AnalisePlanosSaude.Api.Models.Responses;

public sealed record ApiErrorResponse(string Codigo, string Mensagem, IReadOnlyList<string> Detalhes);
