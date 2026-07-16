using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalisePlanosSaude.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnaliseComercialTokenConsulta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenConsulta",
                table: "AnalisesComerciais",
                type: "varchar(48)",
                maxLength: 48,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE `AnalisesComerciais` SET `TokenConsulta` = LOWER(REPLACE(UUID(), '-', '')) WHERE `TokenConsulta` IS NULL OR `TokenConsulta` = '';");

            migrationBuilder.AlterColumn<string>(
                name: "TokenConsulta",
                table: "AnalisesComerciais",
                type: "varchar(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(48)",
                oldMaxLength: 48,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AnalisesComerciais_Status_CriadoEm",
                table: "AnalisesComerciais",
                columns: new[] { "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalisesComerciais_TokenConsulta",
                table: "AnalisesComerciais",
                column: "TokenConsulta",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnalisesComerciais_Status_CriadoEm",
                table: "AnalisesComerciais");

            migrationBuilder.DropIndex(
                name: "IX_AnalisesComerciais_TokenConsulta",
                table: "AnalisesComerciais");

            migrationBuilder.DropColumn(
                name: "TokenConsulta",
                table: "AnalisesComerciais");
        }
    }
}
