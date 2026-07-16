using System.Text.Json.Serialization;
using AnalisePlanosSaude.Api.Data;
using AnalisePlanosSaude.Api.Models.Responses;
using AnalisePlanosSaude.Api.Options;
using AnalisePlanosSaude.Api.Services.Analise;
using AnalisePlanosSaude.Api.Services.AnalisesSimulacao;
using AnalisePlanosSaude.Api.Services.Coleta;
using AnalisePlanosSaude.Api.Services.Coletas;
using AnalisePlanosSaude.Api.Services.OpenRouter;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection("OpenRouter"));
builder.Services.Configure<PlaywrightOptions>(builder.Configuration.GetSection("Playwright"));
builder.Services.Configure<AtualizacaoSimulacoesOptions>(builder.Configuration.GetSection("AtualizacaoSimulacoes"));

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5_000_000;
    options.ValueLengthLimit = 5_000_000;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 5_000_000;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
});

builder.Services.AddHttpClient("OpenRouter");
builder.Services.AddScoped<ISimuladorCollector, SimuladorCollector>();
builder.Services.AddScoped<IOpenRouterService, OpenRouterService>();
builder.Services.AddScoped<IAnaliseService, AnaliseService>();
builder.Services.AddScoped<ISimulacaoColetaService, SimulacaoColetaService>();
builder.Services.AddScoped<IAnaliseSimulacaoService, AnaliseSimulacaoService>();
builder.Services.AddScoped<ISimulacaoHistoricoService, SimulacaoHistoricoService>();
builder.Services.AddScoped<ISimulacaoAtualizacaoService, SimulacaoAtualizacaoService>();
builder.Services.AddHostedService<SimulacaoColetaJobWorker>();
builder.Services.AddHostedService<SimulacaoAtualizacaoDiariaWorker>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, code, message, details) = exception switch
        {
            ValidacaoException validation => (StatusCodes.Status400BadRequest, validation.Codigo, validation.Message, validation.Detalhes),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "REQUISICAO_CANCELADA", "A requisição foi cancelada.", Array.Empty<string>()),
            _ => (StatusCodes.Status500InternalServerError, "ERRO_INTERNO", "Erro interno ao processar a requisição.", exception is null ? [] : new[] { exception.Message })
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(code, message, details));
    });
});

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
