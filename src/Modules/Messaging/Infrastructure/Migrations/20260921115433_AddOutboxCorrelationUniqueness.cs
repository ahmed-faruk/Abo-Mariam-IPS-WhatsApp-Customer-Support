using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxCorrelationUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_outbox_correlation_id",
                schema: "messaging",
                table: "outbox_message",
                column: "correlation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_outbox_correlation_id",
                schema: "messaging",
                table: "outbox_message");
        }
    }
}
