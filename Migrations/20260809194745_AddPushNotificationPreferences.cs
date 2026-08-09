using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BookingNotifications",
                table: "PushSubscriptions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "MessageNotifications",
                table: "PushSubscriptions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "OpenPlayNotifications",
                table: "PushSubscriptions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReminderNotifications",
                table: "PushSubscriptions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingNotifications",
                table: "PushSubscriptions");

            migrationBuilder.DropColumn(
                name: "MessageNotifications",
                table: "PushSubscriptions");

            migrationBuilder.DropColumn(
                name: "OpenPlayNotifications",
                table: "PushSubscriptions");

            migrationBuilder.DropColumn(
                name: "ReminderNotifications",
                table: "PushSubscriptions");
        }
    }
}
