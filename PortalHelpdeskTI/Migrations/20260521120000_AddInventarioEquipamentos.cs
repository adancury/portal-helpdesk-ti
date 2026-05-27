using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260521120000_AddInventarioEquipamentos")]
    public partial class AddInventarioEquipamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventarioEquipamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OrigemCadastro = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NomeEquipamento = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Fabricante = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Modelo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    NumeroSerie = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Patrimonio = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EnderecoIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    EnderecoMac = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Hostname = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SistemaOperacional = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Localizacao = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ProprietarioUsuarioId = table.Column<int>(type: "int", nullable: true),
                    ProprietarioNomeManual = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ProprietarioEmailManual = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ProprietarioDepartamentoManual = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimaDescobertaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventarioEquipamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventarioEquipamentos_Usuarios_ProprietarioUsuarioId",
                        column: x => x.ProprietarioUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventarioEquipamentos_EnderecoIp",
                table: "InventarioEquipamentos",
                column: "EnderecoIp");

            migrationBuilder.CreateIndex(
                name: "IX_InventarioEquipamentos_EnderecoMac",
                table: "InventarioEquipamentos",
                column: "EnderecoMac");

            migrationBuilder.CreateIndex(
                name: "IX_InventarioEquipamentos_NumeroSerie",
                table: "InventarioEquipamentos",
                column: "NumeroSerie");

            migrationBuilder.CreateIndex(
                name: "IX_InventarioEquipamentos_Patrimonio",
                table: "InventarioEquipamentos",
                column: "Patrimonio");

            migrationBuilder.CreateIndex(
                name: "IX_InventarioEquipamentos_ProprietarioUsuarioId",
                table: "InventarioEquipamentos",
                column: "ProprietarioUsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventarioEquipamentos");
        }
    }
}
