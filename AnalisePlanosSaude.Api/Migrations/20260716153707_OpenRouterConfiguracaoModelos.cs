using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalisePlanosSaude.Api.Migrations
{
    /// <inheritdoc />
    public partial class OpenRouterConfiguracaoModelos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenRouterModelosConfiguracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TipoTarefa = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModelId = table.Column<string>(type: "varchar(220)", maxLength: 220, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TravadoManual = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenRouterModelosConfiguracoes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OpenRouterModelosConfiguracoes_ModelId",
                table: "OpenRouterModelosConfiguracoes",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenRouterModelosConfiguracoes_TipoTarefa",
                table: "OpenRouterModelosConfiguracoes",
                column: "TipoTarefa",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenRouterModelosConfiguracoes");
        }
    }
}
