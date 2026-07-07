using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballApi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCourtAndOwnerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OpenTime",
                table: "Courts",
                newName: "SunOpen");

            migrationBuilder.RenameColumn(
                name: "CloseTime",
                table: "Courts",
                newName: "SunClose");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "CourtOwners",
                newName: "Phone");

            migrationBuilder.AddColumn<string>(
                name: "Amenities",
                table: "Courts",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Courts",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "MaxPlayers",
                table: "Courts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "MonFriClose",
                table: "Courts",
                type: "time(6)",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "MonFriOpen",
                table: "Courts",
                type: "time(6)",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SatClose",
                table: "Courts",
                type: "time(6)",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SatOpen",
                table: "Courts",
                type: "time(6)",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<string>(
                name: "SurfaceType",
                table: "Courts",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Courts",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "CourtOwners",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "CourtOwners",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoUrl",
                table: "CourtOwners",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amenities",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "MaxPlayers",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "MonFriClose",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "MonFriOpen",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "SatClose",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "SatOpen",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "SurfaceType",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "CourtOwners");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "CourtOwners");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoUrl",
                table: "CourtOwners");

            migrationBuilder.RenameColumn(
                name: "SunOpen",
                table: "Courts",
                newName: "OpenTime");

            migrationBuilder.RenameColumn(
                name: "SunClose",
                table: "Courts",
                newName: "CloseTime");

            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "CourtOwners",
                newName: "Name");
        }
    }
}
