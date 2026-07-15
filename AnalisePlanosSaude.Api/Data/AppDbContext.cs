using AnalisePlanosSaude.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AnalisePlanosSaude.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Analise> Analises => Set<Analise>();
    public DbSet<AnaliseLink> AnaliseLinks => Set<AnaliseLink>();

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
    }
}
