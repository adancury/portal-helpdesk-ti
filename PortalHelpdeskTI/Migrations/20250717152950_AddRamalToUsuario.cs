using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    public partial class AddRamalToUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CategoriaChamado_TipoChamado_TipoChamadoId1",
                table: "CategoriaChamado");

            migrationBuilder.DropIndex(
                name: "IX_CategoriaChamado_TipoChamadoId1",
                table: "CategoriaChamado");

            migrationBuilder.DropColumn(
                name: "TipoChamadoId1",
                table: "CategoriaChamado");

            migrationBuilder.AddColumn<string>(
                name: "Ramal",
                table: "Usuarios",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ramal",
                table: "Usuarios");

            migrationBuilder.AddColumn<int>(
                name: "TipoChamadoId1",
                table: "CategoriaChamado",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoriaChamado_TipoChamadoId1",
                table: "CategoriaChamado",
                column: "TipoChamadoId1");

            migrationBuilder.AddForeignKey(
                name: "FK_CategoriaChamado_TipoChamado_TipoChamadoId1",
                table: "CategoriaChamado",
                column: "TipoChamadoId1",
                principalTable: "TipoChamado",
                principalColumn: "Id");
        }
    }
}
