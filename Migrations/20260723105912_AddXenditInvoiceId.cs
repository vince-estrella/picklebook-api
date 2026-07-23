using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballApi.Migrations
{
    /// <inheritdoc />
    public partial class AddXenditInvoiceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "XenditInvoiceId",
                table: "Bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "XenditInvoiceId",
                table: "Bookings");
        }
    }
}
