using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalisePlanosSaude.Api.Migrations
{
    /// <inheritdoc />
    public partial class CorrigirDefaultTipoTabelaPlano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE `SimulacoesPlanosVersoes` SET `TipoTabela` = 'NaoInformado' WHERE `TipoTabela` = '' OR `TipoTabela` IS NULL;");
            migrationBuilder.Sql("UPDATE `SimulacoesPlanos` SET `TipoTabela` = 'NaoInformado' WHERE `TipoTabela` = '' OR `TipoTabela` IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "TipoTabela",
                table: "SimulacoesPlanosVersoes",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NaoInformado",
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "TipoTabela",
                table: "SimulacoesPlanos",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NaoInformado",
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TipoTabela",
                table: "SimulacoesPlanosVersoes",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40,
                oldDefaultValue: "NaoInformado")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "TipoTabela",
                table: "SimulacoesPlanos",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40,
                oldDefaultValue: "NaoInformado")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
