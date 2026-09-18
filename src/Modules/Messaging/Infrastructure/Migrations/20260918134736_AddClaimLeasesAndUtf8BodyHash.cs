using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimLeasesAndUtf8BodyHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_body_hash",
                schema: "messaging",
                table: "outbox_message");

            migrationBuilder.AddColumn<DateTime>(
                name: "claim_expires_at",
                schema: "messaging",
                table: "outbox_message",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "claim_token",
                schema: "messaging",
                table: "outbox_message",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "claim_expires_at",
                schema: "messaging",
                table: "inbox_message",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "claim_token",
                schema: "messaging",
                table: "inbox_message",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_body_hash",
                schema: "messaging",
                table: "outbox_message",
                sql: "body_hash = sha256(convert_to(body, 'UTF8'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_body_hash",
                schema: "messaging",
                table: "outbox_message");

            migrationBuilder.DropColumn(
                name: "claim_expires_at",
                schema: "messaging",
                table: "outbox_message");

            migrationBuilder.DropColumn(
                name: "claim_token",
                schema: "messaging",
                table: "outbox_message");

            migrationBuilder.DropColumn(
                name: "claim_expires_at",
                schema: "messaging",
                table: "inbox_message");

            migrationBuilder.DropColumn(
                name: "claim_token",
                schema: "messaging",
                table: "inbox_message");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_body_hash",
                schema: "messaging",
                table: "outbox_message",
                sql: "body_hash = sha256(body::bytea)");
        }
    }
}
