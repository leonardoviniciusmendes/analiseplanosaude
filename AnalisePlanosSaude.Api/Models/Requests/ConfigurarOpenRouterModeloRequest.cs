namespace AnalisePlanosSaude.Api.Models.Requests;

public sealed record ConfigurarOpenRouterModeloRequest(
    string ModelId,
    string? Motivo);
