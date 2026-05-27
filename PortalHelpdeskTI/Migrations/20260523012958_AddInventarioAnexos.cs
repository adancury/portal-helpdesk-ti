using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    public partial class AddInventarioAnexos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventarioAnexos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipamentoInventarioId = table.Column<int>(type: "int", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    NomeArquivo = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    CaminhoRelativo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventarioAnexos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventarioAnexos_InventarioEquipamentos_EquipamentoInventarioId",
                        column: x => x.EquipamentoInventarioId,
                        principalTable: "InventarioEquipamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventarioAnexos_EquipamentoInventarioId",
                table: "InventarioAnexos",
                column: "EquipamentoInventarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventarioAnexos");
        }
    }
}
