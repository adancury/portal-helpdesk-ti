using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenRedefinicaoSenha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TokenExpiraEm",
                table: "Usuarios",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenRedefinicaoSenha",
                table: "Usuarios",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenExpiraEm",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "TokenRedefinicaoSenha",
                table: "Usuarios");
        }
    }
}
