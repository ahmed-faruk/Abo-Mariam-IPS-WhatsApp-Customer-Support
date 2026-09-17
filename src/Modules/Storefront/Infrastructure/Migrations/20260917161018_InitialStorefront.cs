using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialStorefront : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "storefront");

            migrationBuilder.CreateTable(
                name: "business_info",
                schema: "storefront",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    key = table.Column<string>(type: "text", nullable: false),
                    answer_ar = table.Column<string>(type: "text", nullable: false),
                    answer_en = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_info", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_info_key",
                schema: "storefront",
                table: "business_info",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_info",
                schema: "storefront");
        }
    }
}
