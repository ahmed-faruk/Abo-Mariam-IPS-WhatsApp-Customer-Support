using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "conversations");

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    whatsapp_number = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversation",
                schema: "conversations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false, defaultValue: "AI"),
                    window_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_inbound_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_outbound_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation", x => x.Id);
                    table.CheckConstraint("ck_conversation_mode", "mode IN ('AI','Human','Closed')");
                    table.ForeignKey(
                        name: "FK_conversation_customer_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "conversations",
                        principalTable: "customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_state",
                schema: "conversations",
                columns: table => new
                {
                    conversation_id = table.Column<long>(type: "bigint", nullable: false),
                    state_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_state", x => x.conversation_id);
                    table.ForeignKey(
                        name: "FK_conversation_state_conversation_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "conversations",
                        principalTable: "conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_conversation_open_customer",
                schema: "conversations",
                table: "conversation",
                column: "customer_id",
                unique: true,
                filter: "mode <> 'Closed'");

            migrationBuilder.CreateIndex(
                name: "IX_customer_whatsapp_number",
                schema: "conversations",
                table: "customer",
                column: "whatsapp_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_state",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "conversation",
                schema: "conversations");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "conversations");
        }
    }
}
