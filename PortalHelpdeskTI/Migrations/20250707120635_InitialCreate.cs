using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TipoChamado",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipoChamado", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SenhaHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Perfil = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false),
                    DepartamentoId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Usuarios_Departamentos_DepartamentoId",
                        column: x => x.DepartamentoId,
                        principalTable: "Departamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CategoriaChamado",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TipoChamadoId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriaChamado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoriaChamado_TipoChamado_TipoChamadoId",
                        column: x => x.TipoChamadoId,
                        principalTable: "TipoChamado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubcategoriaChamado",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CategoriaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcategoriaChamado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubcategoriaChamado_CategoriaChamado_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "CategoriaChamado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Chamados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Prioridade = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataAbertura = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    TecnicoId = table.Column<int>(type: "int", nullable: true),
                    TipoChamadoId = table.Column<int>(type: "int", nullable: false),
                    CategoriaId = table.Column<int>(type: "int", nullable: false),
                    SubcategoriaId = table.Column<int>(type: "int", nullable: false),
                    VisualizadoPeloTecnico = table.Column<bool>(type: "bit", nullable: false),
                    VisualizadoPeloSolicitante = table.Column<bool>(type: "bit", nullable: false),
                    Solucao = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UsuarioId1 = table.Column<int>(type: "int", nullable: true),
                    UsuarioId2 = table.Column<int>(type: "int", nullable: true),
                    UsuarioId3 = table.Column<int>(type: "int", nullable: true),
                    UsuarioId4 = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chamados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chamados_CategoriaChamado_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "CategoriaChamado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Chamados_SubcategoriaChamado_SubcategoriaId",
                        column: x => x.SubcategoriaId,
                        principalTable: "SubcategoriaChamado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Chamados_TipoChamado_TipoChamadoId",
                        column: x => x.TipoChamadoId,
                        principalTable: "TipoChamado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Chamados_Usuarios_TecnicoId",
                        column: x => x.TecnicoId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Chamados_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Chamados_Usuarios_UsuarioId1",
                        column: x => x.UsuarioId1,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Chamados_Usuarios_UsuarioId2",
                        column: x => x.UsuarioId2,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Chamados_Usuarios_UsuarioId3",
                        column: x => x.UsuarioId3,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Chamados_Usuarios_UsuarioId4",
                        column: x => x.UsuarioId4,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Interacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChamadoId = table.Column<int>(type: "int", nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mensagem = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Interacoes_Chamados_ChamadoId",
                        column: x => x.ChamadoId,
                        principalTable: "Chamados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Interacoes_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Anexos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChamadoId = table.Column<int>(type: "int", nullable: false),
                    CaminhoArquivo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InteracaoId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anexos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Anexos_Chamados_ChamadoId",
                        column: x => x.ChamadoId,
                        principalTable: "Chamados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Anexos_Interacoes_InteracaoId",
                        column: x => x.InteracaoId,
                        principalTable: "Interacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Anexos_ChamadoId",
                table: "Anexos",
                column: "ChamadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Anexos_InteracaoId",
                table: "Anexos",
                column: "InteracaoId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriaChamado_TipoChamadoId",
                table: "CategoriaChamado",
                column: "TipoChamadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_CategoriaId",
                table: "Chamados",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_SubcategoriaId",
                table: "Chamados",
                column: "SubcategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_TecnicoId",
                table: "Chamados",
                column: "TecnicoId");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_TipoChamadoId",
                table: "Chamados",
                column: "TipoChamadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_UsuarioId",
                table: "Chamados",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_UsuarioId1",
                table: "Chamados",
                column: "UsuarioId1");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_UsuarioId2",
                table: "Chamados",
                column: "UsuarioId2");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_UsuarioId3",
                table: "Chamados",
                column: "UsuarioId3");

            migrationBuilder.CreateIndex(
                name: "IX_Chamados_UsuarioId4",
                table: "Chamados",
                column: "UsuarioId4");

            migrationBuilder.CreateIndex(
                name: "IX_Interacoes_ChamadoId",
                table: "Interacoes",
                column: "ChamadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Interacoes_UsuarioId",
                table: "Interacoes",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_SubcategoriaChamado_CategoriaId",
                table: "SubcategoriaChamado",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_DepartamentoId",
                table: "Usuarios",
                column: "DepartamentoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Anexos");

            migrationBuilder.DropTable(
                name: "Interacoes");

            migrationBuilder.DropTable(
                name: "Chamados");

            migrationBuilder.DropTable(
                name: "SubcategoriaChamado");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "CategoriaChamado");

            migrationBuilder.DropTable(
                name: "Departamentos");

            migrationBuilder.DropTable(
                name: "TipoChamado");
        }
    }
}
