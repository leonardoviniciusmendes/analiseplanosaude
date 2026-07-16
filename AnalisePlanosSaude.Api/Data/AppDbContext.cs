using AnalisePlanosSaude.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Analise> Analises => Set<Analise>();
    public DbSet<AnaliseLink> AnaliseLinks => Set<AnaliseLink>();
    public DbSet<SimulacaoColeta> SimulacoesColetas => Set<SimulacaoColeta>();
    public DbSet<SimulacaoPlano> SimulacoesPlanos => Set<SimulacaoPlano>();
    public DbSet<SimulacaoValorFaixa> SimulacoesValoresFaixa => Set<SimulacaoValorFaixa>();
    public DbSet<SimulacaoPrestador> SimulacoesPrestadores => Set<SimulacaoPrestador>();
    public DbSet<SimulacaoJob> SimulacoesJobs => Set<SimulacaoJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Analise>(entity =>
        {
            entity.ToTable("Analises");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Cep).HasMaxLength(8).IsRequired();
            entity.Property(x => x.IdadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.PrioridadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.Observacoes).HasColumnType("text");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.ResultadoJson).HasColumnType("longtext");
            entity.Property(x => x.ResumoCorretor).HasColumnType("longtext");
            entity.Property(x => x.ScriptCorretor).HasColumnType("longtext");
            entity.Property(x => x.MensagemCliente).HasColumnType("longtext");
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasMany(x => x.Links)
                .WithOne(x => x.Analise)
                .HasForeignKey(x => x.AnaliseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnaliseLink>(entity =>
        {
            entity.ToTable("AnaliseLinks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.ConteudoPagina).HasColumnType("longtext");
            entity.Property(x => x.DadosColetadosJson).HasColumnType("longtext");
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => x.AnaliseId);
        });

        modelBuilder.Entity<SimulacaoColeta>(entity =>
        {
            entity.ToTable("SimulacoesColetas");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UrlOriginal).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.HashSimulacao).HasMaxLength(120).IsRequired();
            entity.Property(x => x.EndpointPrincipal).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.EndpointRede).HasMaxLength(2048);
            entity.Property(x => x.JsonPrincipal).HasColumnType("longtext");
            entity.Property(x => x.JsonRede).HasColumnType("longtext");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.Property(x => x.AtualizadoEm).IsRequired();
            entity.HasIndex(x => x.HashSimulacao);
            entity.HasMany(x => x.Planos)
                .WithOne(x => x.SimulacaoColeta)
                .HasForeignKey(x => x.SimulacaoColetaId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Jobs)
                .WithOne(x => x.SimulacaoColeta)
                .HasForeignKey(x => x.SimulacaoColetaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SimulacaoPlano>(entity =>
        {
            entity.ToTable("SimulacoesPlanos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlanoIdExterno).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Nome).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Acomodacao).HasMaxLength(120);
            entity.Property(x => x.ValorTotal).HasPrecision(18, 2);
            entity.Property(x => x.DadosJson).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => new { x.SimulacaoColetaId, x.PlanoIdExterno }).IsUnique();
            entity.HasMany(x => x.ValoresFaixa)
                .WithOne(x => x.SimulacaoPlano)
                .HasForeignKey(x => x.SimulacaoPlanoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Prestadores)
                .WithOne(x => x.SimulacaoPlano)
                .HasForeignKey(x => x.SimulacaoPlanoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SimulacaoValorFaixa>(entity =>
        {
            entity.ToTable("SimulacoesValoresFaixa");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Faixa).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Valor).HasPrecision(18, 2).IsRequired();
            entity.HasIndex(x => x.SimulacaoPlanoId);
        });

        modelBuilder.Entity<SimulacaoPrestador>(entity =>
        {
            entity.ToTable("SimulacoesPrestadores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Tipo).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Nome).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Bairro).HasMaxLength(200);
            entity.Property(x => x.Cidade).HasMaxLength(200);
            entity.Property(x => x.Uf).HasMaxLength(2);
            entity.Property(x => x.Endereco).HasMaxLength(600);
            entity.Property(x => x.EspecialidadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.TextoEvidencia).HasColumnType("text");
            entity.HasIndex(x => x.SimulacaoPlanoId);
            entity.HasIndex(x => new { x.SimulacaoPlanoId, x.Tipo });
        });

        modelBuilder.Entity<SimulacaoJob>(entity =>
        {
            entity.ToTable("SimulacoesJobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(60).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("longtext");
            entity.Property(x => x.ResultadoJson).HasColumnType("longtext");
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => new { x.Status, x.CriadoEm });
            entity.HasIndex(x => new { x.SimulacaoColetaId, x.Tipo }).IsUnique();
        });
    }
}
