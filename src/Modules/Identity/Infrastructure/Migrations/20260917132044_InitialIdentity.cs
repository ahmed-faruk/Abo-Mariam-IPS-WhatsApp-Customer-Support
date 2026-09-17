using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // docs/TECHNICAL.md defines no identity tables yet, so this module owns the schema and
            // its migration history only. Admin login tables arrive with the Identity ticket.
            migrationBuilder.EnsureSchema(
                name: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSchema(
                name: "identity");
        }
    }
}
