using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    conversation_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_external_id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    sender = table.Column<string>(type: "text", nullable: false, defaultValue: "AI"),
                    body = table.Column<string>(type: "text", nullable: false),
                    body_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    provider_message_id = table.Column<string>(type: "text", nullable: true),
                    delivery_status = table.Column<string>(type: "text", nullable: false, defaultValue: "Pending"),
                    partition_key = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    run_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.Id);
                    table.CheckConstraint("ck_outbox_body_hash", "body_hash = sha256(body::bytea)");
                    table.CheckConstraint("ck_outbox_sender", "sender IN ('AI','Agent','System')");
                    table.CheckConstraint("ck_outbox_status", "delivery_status IN ('Pending','Claimed','Sent','Failed','DeadLettered')");
                });

            migrationBuilder.CreateTable(
                name: "webhook_envelope",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    envelope_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    raw_body = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_envelope", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_message",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    envelope_id = table.Column<long>(type: "bigint", nullable: false),
                    provider_message_id = table.Column<string>(type: "text", nullable: false),
                    customer_external_id = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<long>(type: "bigint", nullable: true),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    provider_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processing_status = table.Column<string>(type: "text", nullable: false, defaultValue: "Pending"),
                    partition_key = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    run_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_message", x => x.Id);
                    table.CheckConstraint("ck_inbox_status", "processing_status IN ('Pending','Claimed','Processed','Failed','DeadLettered')");
                    table.ForeignKey(
                        name: "FK_inbox_message_webhook_envelope_envelope_id",
                        column: x => x.envelope_id,
                        principalSchema: "messaging",
                        principalTable: "webhook_envelope",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_claim",
                schema: "messaging",
                table: "inbox_message",
                columns: new[] { "run_after", "Id" },
                filter: "processing_status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_message_envelope",
                schema: "messaging",
                table: "inbox_message",
                column: "envelope_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_message_provider_message_id",
                schema: "messaging",
                table: "inbox_message",
                column: "provider_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_claim",
                schema: "messaging",
                table: "outbox_message",
                columns: new[] { "run_after", "Id" },
                filter: "delivery_status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_message_provider_message_id",
                schema: "messaging",
                table: "outbox_message",
                column: "provider_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_envelope_envelope_hash",
                schema: "messaging",
                table: "webhook_envelope",
                column: "envelope_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_message",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "webhook_envelope",
                schema: "messaging");
        }
    }
}
