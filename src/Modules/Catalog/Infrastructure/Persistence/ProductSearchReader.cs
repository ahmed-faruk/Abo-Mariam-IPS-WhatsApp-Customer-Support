using System.Data.Common;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The structured and lexical search of docs/TECHNICAL.md section 10, expressed as one PostgreSQL
/// statement so eligibility, the per-model collapse and the documented ranking all happen in the
/// database. Nothing is filtered or grouped in application memory, and the result is always bounded.
/// </summary>
/// <remarks>
/// The statement applies the hard filters first, then the admission rules of docs/PLAN.md section 7,
/// then <c>DISTINCT ON (model_id)</c> so a model contributes exactly one recommendation (its best
/// eligible variant under the same ordered keys), and finally the deterministic ranking of ADR-M07,
/// which ends in stable persisted fields. Every text equality the criteria normalize (model code,
/// brand, required ports) is compared through <c>catalog.canonical_text</c> on both sides, so the
/// comparison and the unique model-code index share one canonical definition and stored values that
/// only differ by case or whitespace runs still match.
/// </remarks>
internal sealed class ProductSearchReader(CatalogDbContext dbContext) : IProductSearchReader
{
    /// <summary>The ranked search statement. It is module-internal so the persistence suite can run it directly.</summary>
    internal const string SearchSql = """
        WITH eligible AS (
            SELECT
                m.id AS model_id,
                m.model_code,
                m.brand,
                m.model,
                m.display_name,
                m.size_inches,
                m.panel_type,
                m.resolution_width,
                m.resolution_height,
                m.refresh_rate,
                m.search_tags,
                v.id AS variant_id,
                v.sku,
                v.grade,
                v.selling_price,
                v.quantity,
                v.warranty_days,
                v.warranty_notes,
                v.cosmetic_notes,
                coalesce(
                    (SELECT array_agg(p.port_type ORDER BY p.port_type)
                     FROM catalog.product_model_port AS p
                     WHERE p.product_model_id = m.id),
                    '{}'::text[]) AS ports,
                CASE
                    WHEN @model_code::text IS NOT NULL
                     AND catalog.canonical_text(m.model_code) = catalog.canonical_text(@model_code::text) THEN 0
                    ELSE 1
                END AS exact_rank,
                CASE v.grade WHEN 'A' THEN 1 WHEN 'B' THEN 2 WHEN 'C' THEN 3 ELSE 4 END AS grade_rank,
                CASE
                    WHEN @budget_target::numeric IS NULL THEN 0
                    ELSE abs(v.selling_price - @budget_target::numeric)
                END AS budget_distance,
                CASE
                    WHEN @use_case::text IS NULL THEN 0
                    WHEN EXISTS (
                        SELECT 1 FROM unnest(m.search_tags) AS tag WHERE lower(tag) = @use_case::text)
                        THEN 0
                    ELSE 1
                END AS tag_rank,
                CASE
                    WHEN @text::text IS NULL THEN 0
                    WHEN position(@text::text IN lower(m.display_name)) > 0
                      OR position(@text::text IN lower(m.brand)) > 0
                      OR position(@text::text IN lower(m.model)) > 0
                      OR position(@text::text IN lower(m.model_code)) > 0 THEN 0
                    ELSE 1
                END AS lexical_rank
            FROM catalog.product_variant AS v
            JOIN catalog.product_model AS m ON m.id = v.product_model_id
            WHERE m.is_active
              AND v.is_active
              AND v.quantity > 0
              AND (@model_code::text IS NULL
                   OR catalog.canonical_text(m.model_code) = catalog.canonical_text(@model_code::text))
              AND (@brand::text IS NULL
                   OR catalog.canonical_text(m.brand) = catalog.canonical_text(@brand::text))
              AND (@size_inches::numeric IS NULL
                   OR abs(m.size_inches - @size_inches::numeric) <= @size_tolerance::numeric)
              AND (@panel_type::text IS NULL OR m.panel_type = @panel_type::text)
              AND (@min_resolution_width::integer IS NULL
                   OR m.resolution_width >= @min_resolution_width::integer)
              AND (@min_resolution_height::integer IS NULL
                   OR m.resolution_height >= @min_resolution_height::integer)
              AND (@min_refresh_rate::integer IS NULL OR m.refresh_rate >= @min_refresh_rate::integer)
              AND (@budget_min::numeric IS NULL OR v.selling_price >= @budget_min::numeric)
              AND (@budget_max::numeric IS NULL OR v.selling_price <= @budget_max::numeric)
              AND (cardinality(@required_ports::text[]) = 0
                   OR (SELECT count(DISTINCT catalog.canonical_text(p.port_type))
                       FROM catalog.product_model_port AS p
                       WHERE p.product_model_id = m.id
                         AND catalog.canonical_text(p.port_type) = ANY (
                             SELECT catalog.canonical_text(requested.port_type)
                             FROM unnest(@required_ports::text[]) AS requested(port_type)))
                      = cardinality(@required_ports::text[]))
              AND (cardinality(@grades::text[]) = 0 OR v.grade = ANY(@grades::text[]))
        ),
        best_variant_per_model AS (
            SELECT DISTINCT ON (e.model_id) e.*
            FROM eligible AS e
            ORDER BY e.model_id, e.exact_rank, e.grade_rank, e.budget_distance, e.tag_rank,
                     e.lexical_rank, e.selling_price, e.variant_id
        )
        SELECT
            model_id, model_code, brand, model, display_name, size_inches, panel_type,
            resolution_width, resolution_height, refresh_rate, search_tags,
            variant_id, sku, grade, selling_price, quantity, warranty_days, warranty_notes,
            cosmetic_notes, ports
        FROM best_variant_per_model
        ORDER BY exact_rank, grade_rank, budget_distance, tag_rank, lexical_rank,
                 lower(model_code), model_id, variant_id
        LIMIT @limit;
        """;

    public async Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        CatalogSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var connection = await CatalogSqlCommands.OpenAsync(dbContext, cancellationToken);

        await using var command = CatalogSqlCommands.Create(connection, null, SearchSql);
        CatalogSqlCommands.Add(command, "text", criteria.Text);
        CatalogSqlCommands.Add(command, "model_code", criteria.ModelCode);
        CatalogSqlCommands.Add(command, "brand", criteria.Brand);
        CatalogSqlCommands.Add(command, "size_inches", criteria.SizeInches);
        CatalogSqlCommands.Add(command, "size_tolerance", criteria.SizeToleranceInches);
        CatalogSqlCommands.Add(command, "panel_type", criteria.PanelType);
        CatalogSqlCommands.Add(command, "min_resolution_width", criteria.MinResolutionWidth);
        CatalogSqlCommands.Add(command, "min_resolution_height", criteria.MinResolutionHeight);
        CatalogSqlCommands.Add(command, "min_refresh_rate", criteria.MinRefreshRate);
        CatalogSqlCommands.Add(command, "budget_min", criteria.BudgetMin);
        CatalogSqlCommands.Add(command, "budget_max", criteria.BudgetMax);
        CatalogSqlCommands.Add(command, "budget_target", criteria.BudgetTarget);
        CatalogSqlCommands.Add(command, "use_case", criteria.UseCase);
        CatalogSqlCommands.Add(command, "required_ports", criteria.RequiredPorts.ToArray());
        CatalogSqlCommands.Add(command, "grades", criteria.Grades.ToArray());
        CatalogSqlCommands.Add(command, "limit", criteria.Limit);

        var recommendations = new List<ProductRecommendation>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            recommendations.Add(new ProductRecommendation
            {
                ModelId = reader.GetInt64(0),
                ModelCode = reader.GetString(1),
                Brand = reader.GetString(2),
                Model = reader.GetString(3),
                DisplayName = reader.GetString(4),
                SizeInches = reader.GetDecimal(5),
                PanelType = reader.GetString(6),
                ResolutionWidth = reader.GetInt32(7),
                ResolutionHeight = reader.GetInt32(8),
                RefreshRate = reader.GetInt32(9),
                Tags = TextArray(reader, 10),
                VariantId = reader.GetInt64(11),
                Sku = reader.GetString(12),
                Grade = reader.GetString(13),
                Price = reader.GetDecimal(14),
                Quantity = reader.GetInt32(15),
                WarrantyDays = reader.GetInt32(16),
                WarrantyNotes = reader.IsDBNull(17) ? null : reader.GetString(17),
                CosmeticNotes = reader.IsDBNull(18) ? null : reader.GetString(18),
                Ports = TextArray(reader, 19),
                // The statement only returns active models with active, in-stock variants.
                IsAvailable = true,
            });
        }

        return recommendations;
    }

    private static IReadOnlyList<string> TextArray(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<string[]>(ordinal);
}
