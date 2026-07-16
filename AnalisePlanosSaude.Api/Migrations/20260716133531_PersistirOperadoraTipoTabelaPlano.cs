using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalisePlanosSaude.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersistirOperadoraTipoTabelaPlano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Operadora",
                table: "SimulacoesPlanosVersoes",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TipoTabela",
                table: "SimulacoesPlanosVersoes",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Operadora",
                table: "SimulacoesPlanos",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TipoTabela",
                table: "SimulacoesPlanos",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Operadora",
                table: "SimulacoesPlanosVersoes");

            migrationBuilder.DropColumn(
                name: "TipoTabela",
                table: "SimulacoesPlanosVersoes");

            migrationBuilder.DropColumn(
                name: "Operadora",
                table: "SimulacoesPlanos");

            migrationBuilder.DropColumn(
                name: "TipoTabela",
                table: "SimulacoesPlanos");
        }
    }
}
