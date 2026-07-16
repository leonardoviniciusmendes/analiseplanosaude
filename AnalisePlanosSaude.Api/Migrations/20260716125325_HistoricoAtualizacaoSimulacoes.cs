using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalisePlanosSaude.Api.Migrations
{
    /// <inheritdoc />
    public partial class HistoricoAtualizacaoSimulacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SimulacoesAtualizacoesJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SimulacaoColetaId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Motivo = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    MaxTentativas = table.Column<int>(type: "int", nullable: false),
                    VersaoGerada = table.Column<int>(type: "int", nullable: true),
                    DiffJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Erro = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IniciadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FinalizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulacoesAtualizacoesJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulacoesAtualizacoesJobs_SimulacoesColetas_SimulacaoColeta~",
                        column: x => x.SimulacaoColetaId,
                        principalTable: "SimulacoesColetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SimulacoesColetasVersoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SimulacaoColetaId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JsonPrincipal = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JsonRede = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HashConteudo = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiffJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ProcessadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulacoesColetasVersoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulacoesColetasVersoes_SimulacoesColetas_SimulacaoColetaId",
                        column: x => x.SimulacaoColetaId,
                        principalTable: "SimulacoesColetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SimulacoesPlanosVersoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SimulacaoColetaVersaoId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlanoIdExterno = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nome = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Acomodacao = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValorTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DadosJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulacoesPlanosVersoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulacoesPlanosVersoes_SimulacoesColetasVersoes_SimulacaoCo~",
                        column: x => x.SimulacaoColetaVersaoId,
                        principalTable: "SimulacoesColetasVersoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SimulacoesPrestadoresVersoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SimulacaoPlanoVersaoId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Tipo = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nome = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Bairro = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Cidade = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Uf = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Endereco = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EspecialidadesJson = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TextoEvidencia = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulacoesPrestadoresVersoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulacoesPrestadoresVersoes_SimulacoesPlanosVersoes_Simulac~",
                        column: x => x.SimulacaoPlanoVersaoId,
                        principalTable: "SimulacoesPlanosVersoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SimulacoesValoresFaixaVersoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SimulacaoPlanoVersaoId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Faixa = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdadeMin = table.Column<int>(type: "int", nullable: true),
                    IdadeMax = table.Column<int>(type: "int", nullable: true),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulacoesValoresFaixaVersoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulacoesValoresFaixaVersoes_SimulacoesPlanosVersoes_Simula~",
                        column: x => x.SimulacaoPlanoVersaoId,
                        principalTable: "SimulacoesPlanosVersoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesAtualizacoesJobs_SimulacaoColetaId",
                table: "SimulacoesAtualizacoesJobs",
                column: "SimulacaoColetaId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesAtualizacoesJobs_Status_CriadoEm",
                table: "SimulacoesAtualizacoesJobs",
                columns: new[] { "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesColetasVersoes_HashConteudo",
                table: "SimulacoesColetasVersoes",
                column: "HashConteudo");

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesColetasVersoes_SimulacaoColetaId_Versao",
                table: "SimulacoesColetasVersoes",
                columns: new[] { "SimulacaoColetaId", "Versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesPlanosVersoes_SimulacaoColetaVersaoId_PlanoIdExter~",
                table: "SimulacoesPlanosVersoes",
                columns: new[] { "SimulacaoColetaVersaoId", "PlanoIdExterno" });

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesPrestadoresVersoes_SimulacaoPlanoVersaoId",
                table: "SimulacoesPrestadoresVersoes",
                column: "SimulacaoPlanoVersaoId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesPrestadoresVersoes_SimulacaoPlanoVersaoId_Tipo",
                table: "SimulacoesPrestadoresVersoes",
                columns: new[] { "SimulacaoPlanoVersaoId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesValoresFaixaVersoes_SimulacaoPlanoVersaoId",
                table: "SimulacoesValoresFaixaVersoes",
                column: "SimulacaoPlanoVersaoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SimulacoesAtualizacoesJobs");

            migrationBuilder.DropTable(
                name: "SimulacoesPrestadoresVersoes");

            migrationBuilder.DropTable(
                name: "SimulacoesValoresFaixaVersoes");

            migrationBuilder.DropTable(
                name: "SimulacoesPlanosVersoes");

            migrationBuilder.DropTable(
                name: "SimulacoesColetasVersoes");
        }
    }
}
