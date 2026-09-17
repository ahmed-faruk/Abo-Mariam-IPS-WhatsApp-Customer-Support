using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    old_json = table.Column<string>(type: "jsonb", nullable: true),
                    new_json = table.Column<string>(type: "jsonb", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_model",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    model_code = table.Column<string>(type: "text", nullable: false),
                    brand = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    size_inches = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    panel_type = table.Column<string>(type: "text", nullable: false),
                    resolution_width = table.Column<int>(type: "integer", nullable: false),
                    resolution_height = table.Column<int>(type: "integer", nullable: false),
                    refresh_rate = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    search_tags = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_model", x => x.Id);
                    table.CheckConstraint("ck_model_panel", "panel_type IN ('IPS','TN','VA','OLED','Other')");
                    table.CheckConstraint("ck_model_refresh", "refresh_rate BETWEEN 24 AND 500");
                    table.CheckConstraint("ck_model_resolution", "resolution_width > 0 AND resolution_height > 0");
                    table.CheckConstraint("ck_model_size", "size_inches BETWEEN 10 AND 60");
                });

            migrationBuilder.CreateTable(
                name: "product_model_port",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    product_model_id = table.Column<long>(type: "bigint", nullable: false),
                    port_type = table.Column<string>(type: "text", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_model_port", x => x.Id);
                    table.CheckConstraint("ck_port_count", "count BETWEEN 1 AND 16");
                    table.ForeignKey(
                        name: "FK_product_model_port_product_model_product_model_id",
                        column: x => x.product_model_id,
                        principalSchema: "catalog",
                        principalTable: "product_model",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variant",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    product_model_id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<string>(type: "text", nullable: false),
                    selling_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    warranty_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    warranty_notes = table.Column<string>(type: "text", nullable: true),
                    cosmetic_notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variant", x => x.Id);
                    table.CheckConstraint("ck_variant_grade", "grade IN ('A','B','C')");
                    table.CheckConstraint("ck_variant_price", "selling_price >= 0");
                    table.CheckConstraint("ck_variant_quantity", "quantity >= 0");
                    table.CheckConstraint("ck_variant_warranty", "warranty_days >= 0");
                    table.ForeignKey(
                        name: "FK_product_variant_product_model_product_model_id",
                        column: x => x.product_model_id,
                        principalSchema: "catalog",
                        principalTable: "product_model",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_model_model_code",
                schema: "catalog",
                table: "product_model",
                column: "model_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_port_model",
                schema: "catalog",
                table: "product_model_port",
                column: "product_model_id");

            migrationBuilder.CreateIndex(
                name: "uq_model_port",
                schema: "catalog",
                table: "product_model_port",
                columns: new[] { "product_model_id", "port_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_variant_sku",
                schema: "catalog",
                table: "product_variant",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_variant_model",
                schema: "catalog",
                table: "product_variant",
                column: "product_model_id");

            migrationBuilder.CreateIndex(
                name: "uq_variant_model_grade",
                schema: "catalog",
                table: "product_variant",
                columns: new[] { "product_model_id", "grade" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_model_port",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_variant",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_model",
                schema: "catalog");
        }
    }
}
