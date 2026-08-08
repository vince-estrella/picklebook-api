using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballApi.Migrations
{
    /// <inheritdoc />
    public partial class AddVenues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VenueId",
                table: "Courts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Latitude = table.Column<double>(type: "double", nullable: false),
                    Longitude = table.Column<double>(type: "double", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amenities = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalBookingUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CourtOwnerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Venues_CourtOwners_CourtOwnerId",
                        column: x => x.CourtOwnerId,
                        principalTable: "CourtOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(@"
                INSERT INTO `Venues` (`Name`, `Address`, `Latitude`, `Longitude`, `Description`, `Amenities`, `ExternalBookingUrl`, `CourtOwnerId`)
                SELECT
                    LEFT(
                        CASE
                            WHEN COUNT(*) > 1 THEN COALESCE(NULLIF(`Address`, ''), 'Pickleball Venue')
                            ELSE COALESCE(NULLIF(MIN(`Name`), ''), COALESCE(NULLIF(`Address`, ''), 'Pickleball Venue'))
                        END,
                        255
                    ) AS `Name`,
                    COALESCE(`Address`, '') AS `Address`,
                    `Latitude`,
                    `Longitude`,
                    COALESCE(MIN(`Description`), '') AS `Description`,
                    COALESCE(MIN(`Amenities`), '') AS `Amenities`,
                    MIN(`ExternalBookingUrl`) AS `ExternalBookingUrl`,
                    `CourtOwnerId`
                FROM `Courts`
                GROUP BY `CourtOwnerId`, `Address`, `Latitude`, `Longitude`;
            ");

            migrationBuilder.Sql(@"
                UPDATE `Courts` c
                INNER JOIN `Venues` v
                    ON v.`CourtOwnerId` = c.`CourtOwnerId`
                    AND v.`Address` = COALESCE(c.`Address`, '')
                    AND v.`Latitude` = c.`Latitude`
                    AND v.`Longitude` = c.`Longitude`
                SET c.`VenueId` = v.`Id`;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Courts_VenueId",
                table: "Courts",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_Venues_CourtOwnerId_Name",
                table: "Venues",
                columns: new[] { "CourtOwnerId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_Courts_Venues_VenueId",
                table: "Courts",
                column: "VenueId",
                principalTable: "Venues",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courts_Venues_VenueId",
                table: "Courts");

            migrationBuilder.DropTable(
                name: "Venues");

            migrationBuilder.DropIndex(
                name: "IX_Courts_VenueId",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "VenueId",
                table: "Courts");
        }
    }
}
