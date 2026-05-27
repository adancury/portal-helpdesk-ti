using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    public partial class AddCampoDataConclusao2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DataConclusao",
                table: "Chamados",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataConclusao",
                table: "Chamados");
        }
    }
}
