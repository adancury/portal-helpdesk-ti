using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalHelpdeskTI.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.CreateTable(
            //    name: "SmtpConfiguracoes",
            //    columns: table => new
            //    {
            //        Id = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        SmtpServer = table.Column<string>(type: "nvarchar(max)", nullable: false),
            //        SmtpPort = table.Column<int>(type: "int", nullable: false),
            //        SmtpUser = table.Column<string>(type: "nvarchar(max)", nullable: false),
            //        SmtpPass = table.Column<string>(type: "nvarchar(max)", nullable: false),
            //        FromEmail = table.Column<string>(type: "nvarchar(max)", nullable: false)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_SmtpConfiguracoes", x => x.Id);
            //    });

            migrationBuilder.CreateTable(
                name: "TemplatesEmail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Assunto = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CorpoHtml = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplatesEmail", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.DropTable(
            //    name: "SmtpConfiguracoes");

            migrationBuilder.DropTable(
                name: "TemplatesEmail");
        }
    }
}
