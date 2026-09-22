using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationModeRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "mode_revision",
                schema: "conversations",
                table: "conversation",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mode_revision",
                schema: "conversations",
                table: "conversation");
        }
    }
}
