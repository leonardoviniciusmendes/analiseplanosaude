using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalisePlanosSaude.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlanoIdExternoUnicoGlobal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesPlanos_SimulacaoColetaId",
                table: "SimulacoesPlanos",
                column: "SimulacaoColetaId");

            migrationBuilder.DropIndex(
                name: "IX_SimulacoesPlanos_SimulacaoColetaId_PlanoIdExterno",
                table: "SimulacoesPlanos");

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesPlanos_PlanoIdExterno",
                table: "SimulacoesPlanos",
                column: "PlanoIdExterno",
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SimulacoesPlanos_PlanoIdExterno",
                table: "SimulacoesPlanos");

            migrationBuilder.CreateIndex(
                name: "IX_SimulacoesPlanos_SimulacaoColetaId_PlanoIdExterno",
                table: "SimulacoesPlanos",
                columns: new[] { "SimulacaoColetaId", "PlanoIdExterno" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_SimulacoesPlanos_SimulacaoColetaId",
                table: "SimulacoesPlanos");
        }
    }
}
