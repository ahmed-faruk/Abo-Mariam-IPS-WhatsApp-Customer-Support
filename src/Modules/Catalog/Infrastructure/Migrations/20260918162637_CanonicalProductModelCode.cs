using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// Enforces the logical uniqueness of a model code with the same canonical comparison the search
    /// uses: trimming, collapsing whitespace runs and case folding.
    /// </summary>
    /// <remarks>
    /// The catalog owns the canonical comparison as one immutable PostgreSQL function, so the unique
    /// index and the search statement cannot drift apart and <c>P2419H</c>, <c>p2419h</c> and
    /// <c>"  P2419H  "</c> can no longer coexist. The statements are raw SQL because EF Core cannot map
    /// an index on a function expression; the case-sensitive unique index of InitialCatalog stays in
    /// place, and applying this migration fails loudly if existing rows already collide, because such
    /// data would make an exact lookup ambiguous.
    /// </remarks>
    public partial class CanonicalProductModelCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION catalog.canonical_text(value text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE
                    AS $$ SELECT btrim(regexp_replace(lower(value), '\s+', ' ', 'g')) $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX uq_product_model_canonical_code
                    ON catalog.product_model (catalog.canonical_text(model_code));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS catalog.uq_product_model_canonical_code;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS catalog.canonical_text(text);");
        }
    }
}
