using AnalisePlanosSaude.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Analise> Analises => Set<Analise>();
    public DbSet<AnaliseLink> AnaliseLinks => Set<AnaliseLink>();
    public DbSet<AnaliseComercial> AnalisesComerciais => Set<AnaliseComercial>();
    public DbSet<SimulacaoColeta> SimulacoesColetas => Set<SimulacaoColeta>();
    public DbSet<SimulacaoPlano> SimulacoesPlanos => Set<SimulacaoPlano>();
    public DbSet<SimulacaoValorFaixa> SimulacoesValoresFaixa => Set<SimulacaoValorFaixa>();
    public DbSet<SimulacaoPrestador> SimulacoesPrestadores => Set<SimulacaoPrestador>();
    public DbSet<SimulacaoJob> SimulacoesJobs => Set<SimulacaoJob>();
    public DbSet<SimulacaoAnalise> SimulacoesAnalises => Set<SimulacaoAnalise>();
    public DbSet<SimulacaoAtualizacaoJob> SimulacoesAtualizacoesJobs => Set<SimulacaoAtualizacaoJob>();
    public DbSet<SimulacaoColetaVersao> SimulacoesColetasVersoes => Set<SimulacaoColetaVersao>();
    public DbSet<SimulacaoPlanoVersao> SimulacoesPlanosVersoes => Set<SimulacaoPlanoVersao>();
    public DbSet<SimulacaoValorFaixaVersao> SimulacoesValoresFaixaVersoes => Set<SimulacaoValorFaixaVersao>();
    public DbSet<SimulacaoPrestadorVersao> SimulacoesPrestadoresVersoes => Set<SimulacaoPrestadorVersao>();
    public DbSet<OpenRouterModelo> OpenRouterModelos => Set<OpenRouterModelo>();
    public DbSet<OpenRouterModeloHistorico> OpenRouterModelosHistorico => Set<OpenRouterModeloHistorico>();
    public DbSet<OpenRouterExecucao> OpenRouterExecucoes => Set<OpenRouterExecucao>();
    public DbSet<OpenRouterModeloConfiguracao> OpenRouterModelosConfiguracoes => Set<OpenRouterModeloConfiguracao>();

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

        modelBuilder.Entity<AnaliseComercial>(entity =>
        {
            entity.ToTable("AnalisesComerciais");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenConsulta).HasMaxLength(48).IsRequired();
            entity.Property(x => x.IdadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.NecessidadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.Cep).HasMaxLength(8);
            entity.Property(x => x.LinkSimulacao).HasMaxLength(2048);
            entity.Property(x => x.HashSimulacao).HasMaxLength(120);
            entity.Property(x => x.FiltrosJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.PerfilCliente).HasColumnType("text");
            entity.Property(x => x.PrioridadeVenda).HasMaxLength(80);
            entity.Property(x => x.ObservacoesCorretor).HasColumnType("text");
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.DatasetJson).HasColumnType("longtext");
            entity.Property(x => x.ResultadoJson).HasColumnType("longtext");
            entity.Property(x => x.MelhorPlanoCliente).HasMaxLength(300);
            entity.Property(x => x.MelhorPlanoCorretor).HasMaxLength(300);
            entity.Property(x => x.MensagemCaptacao).HasColumnType("longtext");
            entity.Property(x => x.MensagemApresentacao).HasColumnType("longtext");
            entity.Property(x => x.MensagemFechamento).HasColumnType("longtext");
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => x.TokenConsulta).IsUnique();
            entity.HasIndex(x => x.HashSimulacao);
            entity.HasIndex(x => x.CriadoEm);
            entity.HasIndex(x => new { x.Status, x.CriadoEm });
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
            entity.HasMany(x => x.Atualizacoes)
                .WithOne(x => x.SimulacaoColeta)
                .HasForeignKey(x => x.SimulacaoColetaId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Versoes)
                .WithOne(x => x.SimulacaoColeta)
                .HasForeignKey(x => x.SimulacaoColetaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SimulacaoPlano>(entity =>
        {
            entity.ToTable("SimulacoesPlanos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlanoIdExterno).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Operadora).HasMaxLength(160);
            entity.Property(x => x.TipoTabela).HasConversion<string>().HasMaxLength(40).HasDefaultValue(TipoTabelaPlano.NaoInformado).IsRequired();
            entity.Property(x => x.Nome).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Acomodacao).HasMaxLength(120);
            entity.Property(x => x.ValorTotal).HasPrecision(18, 2);
            entity.Property(x => x.DadosJson).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => x.PlanoIdExterno).IsUnique();
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

        modelBuilder.Entity<SimulacaoAnalise>(entity =>
        {
            entity.ToTable("SimulacoesAnalises");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LinkOriginal).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.HashSimulacao).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IdadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.FaixasUtilizadasJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.PrioridadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.Observacoes).HasColumnType("text");
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.DatasetJson).HasColumnType("longtext");
            entity.Property(x => x.ResultadoJson).HasColumnType("longtext");
            entity.Property(x => x.ResumoCorretor).HasColumnType("longtext");
            entity.Property(x => x.ScriptCorretor).HasColumnType("longtext");
            entity.Property(x => x.MensagemWhatsApp).HasColumnType("longtext");
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => x.HashSimulacao);
            entity.HasOne(x => x.SimulacaoColeta)
                .WithMany()
                .HasForeignKey(x => x.SimulacaoColetaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SimulacaoAtualizacaoJob>(entity =>
        {
            entity.ToTable("SimulacoesAtualizacoesJobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.Motivo).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DiffJson).HasColumnType("longtext");
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => new { x.Status, x.CriadoEm });
            entity.HasIndex(x => x.SimulacaoColetaId);
        });

        modelBuilder.Entity<SimulacaoColetaVersao>(entity =>
        {
            entity.ToTable("SimulacoesColetasVersoes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.JsonPrincipal).HasColumnType("longtext");
            entity.Property(x => x.JsonRede).HasColumnType("longtext");
            entity.Property(x => x.HashConteudo).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DiffJson).HasColumnType("longtext").IsRequired();
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => new { x.SimulacaoColetaId, x.Versao }).IsUnique();
            entity.HasIndex(x => x.HashConteudo);
            entity.HasMany(x => x.Planos)
                .WithOne(x => x.SimulacaoColetaVersao)
                .HasForeignKey(x => x.SimulacaoColetaVersaoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SimulacaoPlanoVersao>(entity =>
        {
            entity.ToTable("SimulacoesPlanosVersoes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlanoIdExterno).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Operadora).HasMaxLength(160);
            entity.Property(x => x.TipoTabela).HasConversion<string>().HasMaxLength(40).HasDefaultValue(TipoTabelaPlano.NaoInformado).IsRequired();
            entity.Property(x => x.Nome).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Acomodacao).HasMaxLength(120);
            entity.Property(x => x.ValorTotal).HasPrecision(18, 2);
            entity.Property(x => x.DadosJson).HasColumnType("longtext");
            entity.HasIndex(x => new { x.SimulacaoColetaVersaoId, x.PlanoIdExterno });
            entity.HasMany(x => x.ValoresFaixa)
                .WithOne(x => x.SimulacaoPlanoVersao)
                .HasForeignKey(x => x.SimulacaoPlanoVersaoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Prestadores)
                .WithOne(x => x.SimulacaoPlanoVersao)
                .HasForeignKey(x => x.SimulacaoPlanoVersaoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SimulacaoValorFaixaVersao>(entity =>
        {
            entity.ToTable("SimulacoesValoresFaixaVersoes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Faixa).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Valor).HasPrecision(18, 2).IsRequired();
            entity.HasIndex(x => x.SimulacaoPlanoVersaoId);
        });

        modelBuilder.Entity<SimulacaoPrestadorVersao>(entity =>
        {
            entity.ToTable("SimulacoesPrestadoresVersoes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Tipo).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Nome).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Bairro).HasMaxLength(200);
            entity.Property(x => x.Cidade).HasMaxLength(200);
            entity.Property(x => x.Uf).HasMaxLength(2);
            entity.Property(x => x.Endereco).HasMaxLength(600);
            entity.Property(x => x.EspecialidadesJson).HasColumnType("json").IsRequired();
            entity.Property(x => x.TextoEvidencia).HasColumnType("text");
            entity.HasIndex(x => x.SimulacaoPlanoVersaoId);
            entity.HasIndex(x => new { x.SimulacaoPlanoVersaoId, x.Tipo });
        });

        modelBuilder.Entity<OpenRouterModelo>(entity =>
        {
            entity.ToTable("OpenRouterModelos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ModelId).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Nome).HasMaxLength(300);
            entity.Property(x => x.Provider).HasMaxLength(120);
            entity.Property(x => x.PrecoInputPorMilhaoTokens).HasPrecision(18, 8);
            entity.Property(x => x.PrecoOutputPorMilhaoTokens).HasPrecision(18, 8);
            entity.Property(x => x.CustoBeneficioScore).HasPrecision(18, 4);
            entity.Property(x => x.UltimaAtualizacao).IsRequired();
            entity.HasIndex(x => x.ModelId).IsUnique();
            entity.HasIndex(x => new { x.Ativo, x.CustoBeneficioScore });
        });

        modelBuilder.Entity<OpenRouterModeloHistorico>(entity =>
        {
            entity.ToTable("OpenRouterModelosHistorico");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ModelId).HasMaxLength(220).IsRequired();
            entity.Property(x => x.PrecoInputPorMilhaoTokens).HasPrecision(18, 8);
            entity.Property(x => x.PrecoOutputPorMilhaoTokens).HasPrecision(18, 8);
            entity.Property(x => x.CustoBeneficioScore).HasPrecision(18, 4);
            entity.Property(x => x.DadosJson).HasColumnType("longtext").IsRequired();
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => new { x.ModelId, x.CriadoEm });
        });

        modelBuilder.Entity<OpenRouterExecucao>(entity =>
        {
            entity.ToTable("OpenRouterExecucoes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TipoTarefa).HasConversion<string>().HasMaxLength(60).IsRequired();
            entity.Property(x => x.ModelId).HasMaxLength(220).IsRequired();
            entity.Property(x => x.CustoEstimado).HasPrecision(18, 8);
            entity.Property(x => x.Erro).HasColumnType("longtext");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.HasIndex(x => new { x.TipoTarefa, x.CriadoEm });
            entity.HasIndex(x => new { x.ModelId, x.CriadoEm });
        });

        modelBuilder.Entity<OpenRouterModeloConfiguracao>(entity =>
        {
            entity.ToTable("OpenRouterModelosConfiguracoes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TipoTarefa).HasConversion<string>().HasMaxLength(60).IsRequired();
            entity.Property(x => x.ModelId).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Motivo).HasColumnType("text");
            entity.Property(x => x.CriadoEm).IsRequired();
            entity.Property(x => x.AtualizadoEm).IsRequired();
            entity.HasIndex(x => x.TipoTarefa).IsUnique();
            entity.HasIndex(x => x.ModelId);
        });
    }
}
