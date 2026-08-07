using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballApi.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenPlaySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowOpenPlay",
                table: "Courts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "BookingType",
                table: "Bookings",
                type: "longtext",
                nullable: false,
                defaultValue: "Standard")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "OpenPlayMaxPlayers",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenPlayNote",
                table: "Bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "OpenPlayPricePerPlayer",
                table: "Bookings",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenPlayReclubLink",
                table: "Bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OpenPlaySkillLevel",
                table: "Bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OpenPlaySessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    HostUserId = table.Column<int>(type: "int", nullable: true),
                    HostOwnerId = table.Column<int>(type: "int", nullable: true),
                    RoomCode = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlaySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlaySessions_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpenPlaySessions_CourtOwners_HostOwnerId",
                        column: x => x.HostOwnerId,
                        principalTable: "CourtOwners",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpenPlaySessions_Users_HostUserId",
                        column: x => x.HostUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OpenPlayParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OpenPlaySessionId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CheckInStatus = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlayParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlayParticipants_OpenPlaySessions_OpenPlaySessionId",
                        column: x => x.OpenPlaySessionId,
                        principalTable: "OpenPlaySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpenPlayParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayParticipants_OpenPlaySessionId_UserId",
                table: "OpenPlayParticipants",
                columns: new[] { "OpenPlaySessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlayParticipants_UserId",
                table: "OpenPlayParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySessions_BookingId",
                table: "OpenPlaySessions",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySessions_HostOwnerId",
                table: "OpenPlaySessions",
                column: "HostOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySessions_HostUserId",
                table: "OpenPlaySessions",
                column: "HostUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySessions_RoomCode",
                table: "OpenPlaySessions",
                column: "RoomCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenPlayParticipants");

            migrationBuilder.DropTable(
                name: "OpenPlaySessions");

            migrationBuilder.DropColumn(
                name: "AllowOpenPlay",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "BookingType",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OpenPlayMaxPlayers",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OpenPlayNote",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OpenPlayPricePerPlayer",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OpenPlayReclubLink",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OpenPlaySkillLevel",
                table: "Bookings");
        }
    }
}
