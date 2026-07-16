using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalisePlanosSaude.Api.Migrations
{
    /// <inheritdoc />
    public partial class OpenRouterCatalogoModelos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenRouterExecucoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TipoTarefa = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModelId = table.Column<string>(type: "varchar(220)", maxLength: 220, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TokensInput = table.Column<int>(type: "int", nullable: true),
                    TokensOutput = table.Column<int>(type: "int", nullable: true),
                    CustoEstimado = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    TempoMs = table.Column<long>(type: "bigint", nullable: false),
                    Sucesso = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Erro = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenRouterExecucoes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OpenRouterModelos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<string>(type: "varchar(220)", maxLength: 220, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nome = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Provider = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContextLength = table.Column<int>(type: "int", nullable: true),
                    PrecoInputPorMilhaoTokens = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    PrecoOutputPorMilhaoTokens = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    SuportaJsonEstruturado = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SuportaTools = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RecomendadoNormalizacao = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RecomendadoAnalise = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RecomendadoMensagem = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CustoBeneficioScore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalExecucoes = table.Column<int>(type: "int", nullable: false),
                    TotalSucessos = table.Column<int>(type: "int", nullable: false),
                    TotalFalhas = table.Column<int>(type: "int", nullable: false),
                    TempoMedioMs = table.Column<double>(type: "double", nullable: true),
                    UltimaAtualizacao = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenRouterModelos", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OpenRouterModelosHistorico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<string>(type: "varchar(220)", maxLength: 220, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrecoInputPorMilhaoTokens = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    PrecoOutputPorMilhaoTokens = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    ContextLength = table.Column<int>(type: "int", nullable: true),
                    Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CustoBeneficioScore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DadosJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenRouterModelosHistorico", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OpenRouterExecucoes_ModelId_CriadoEm",
                table: "OpenRouterExecucoes",
                columns: new[] { "ModelId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenRouterExecucoes_TipoTarefa_CriadoEm",
                table: "OpenRouterExecucoes",
                columns: new[] { "TipoTarefa", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenRouterModelos_Ativo_CustoBeneficioScore",
                table: "OpenRouterModelos",
                columns: new[] { "Ativo", "CustoBeneficioScore" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenRouterModelos_ModelId",
                table: "OpenRouterModelos",
                column: "ModelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenRouterModelosHistorico_ModelId_CriadoEm",
                table: "OpenRouterModelosHistorico",
                columns: new[] { "ModelId", "CriadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenRouterExecucoes");

            migrationBuilder.DropTable(
                name: "OpenRouterModelos");

            migrationBuilder.DropTable(
                name: "OpenRouterModelosHistorico");
        }
    }
}
