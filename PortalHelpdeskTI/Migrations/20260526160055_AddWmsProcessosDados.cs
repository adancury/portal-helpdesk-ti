using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsProcessosDados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsProcessos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ChaveProcesso = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ChaveItem = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    StatusAnterior = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DataReferencia = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CodProprietario = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NomeProprietario = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    NumeroPedido = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CodigoProduto = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DescricaoProduto = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ClienteFornecedor = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    UsuarioResponsavel = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    QuantidadePrevista = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    QuantidadeExecutada = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    QuantidadeDivergente = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PayloadHash = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimaSincronizacaoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsProcessos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsSyncExecucoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InicioEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FimEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RegistrosRecebidos = table.Column<int>(type: "int", nullable: false),
                    RegistrosNovos = table.Column<int>(type: "int", nullable: false),
                    RegistrosAlterados = table.Column<int>(type: "int", nullable: false),
                    Mensagem = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsSyncExecucoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsProcessoLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WmsProcessoId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ChaveProcesso = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ChaveItem = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Evento = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StatusAnterior = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    StatusNovo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CamposAlteradosJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsProcessoLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsProcessoLogs_WmsProcessos_WmsProcessoId",
                        column: x => x.WmsProcessoId,
                        principalTable: "WmsProcessos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsProcessoLogs_Tipo_ChaveProcesso",
                table: "WmsProcessoLogs",
                columns: new[] { "Tipo", "ChaveProcesso" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsProcessoLogs_WmsProcessoId_CriadoEm",
                table: "WmsProcessoLogs",
                columns: new[] { "WmsProcessoId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsProcessos_DataReferencia",
                table: "WmsProcessos",
                column: "DataReferencia");

            migrationBuilder.CreateIndex(
                name: "IX_WmsProcessos_NumeroDocumento",
                table: "WmsProcessos",
                column: "NumeroDocumento");

            migrationBuilder.CreateIndex(
                name: "IX_WmsProcessos_NumeroPedido",
                table: "WmsProcessos",
                column: "NumeroPedido");

            migrationBuilder.CreateIndex(
                name: "IX_WmsProcessos_Tipo_ChaveItem",
                table: "WmsProcessos",
                columns: new[] { "Tipo", "ChaveItem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsProcessos_Tipo_Status",
                table: "WmsProcessos",
                columns: new[] { "Tipo", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsSyncExecucoes_Tipo_InicioEm",
                table: "WmsSyncExecucoes",
                columns: new[] { "Tipo", "InicioEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsProcessoLogs");

            migrationBuilder.DropTable(
                name: "WmsSyncExecucoes");

            migrationBuilder.DropTable(
                name: "WmsProcessos");
        }
    }
}
