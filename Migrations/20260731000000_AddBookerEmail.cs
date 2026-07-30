using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBookerEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookerEmail",
                table: "Bookings",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookerEmail",
                table: "Bookings");
        }
    }
}
