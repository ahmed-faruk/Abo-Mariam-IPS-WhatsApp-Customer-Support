using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxApplicationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "application_metadata",
                schema: "messaging",
                table: "outbox_message",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "application_metadata",
                schema: "messaging",
                table: "outbox_message");
        }
    }
}
