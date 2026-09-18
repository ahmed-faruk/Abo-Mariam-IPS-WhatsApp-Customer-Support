using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// Issue #6 acceptance: every commercial change and its audit row commit atomically, a no-op writes
/// nothing, and a failure leaves both the business state and the audit table untouched.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogCommercialUpdateTests(PostgresContainerFixture postgres) : CatalogFixture(postgres)
{
    private const string Actor = "admin-42";

    [Fact]
    public async Task A_price_change_and_its_audit_row_commit_together()
    {
        var variant = await AddVariantAsync(await AddModelAsync("P2419H"), "SKU-P2419H", price: 4200m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Commercials(scope.ServiceProvider).UpdatePriceAsync(
            new VariantPriceUpdate(variant, 3999.5m, Actor));

        Assert.Equal(CommercialUpdateOutcome.Updated, outcome);
        Assert.Equal("3999.50", await StoredPriceAsync(variant));

        var audit = Assert.Single(await AuditsAsync());

        Assert.Equal("ProductVariant", audit.EntityType);
        Assert.Equal(variant, audit.EntityId);
        Assert.Equal("UpdatePrice", audit.Action);
        Assert.Equal("""{"selling_price": 4200.00}""", audit.OldJson);
        Assert.Equal("""{"selling_price": 3999.50}""", audit.NewJson);
        Assert.Equal(Actor, audit.UserId);
        Assert.NotEmpty(audit.CreatedAt);
    }

    [Fact]
    public async Task The_audit_records_the_value_postgresql_actually_stored()
    {
        var variant = await AddVariantAsync(await AddModelAsync("P2419H"), "SKU-P2419H", price: 4200m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        // The column keeps two decimals, so the audited new value must be the stored one.
        await Commercials(scope.ServiceProvider).UpdatePriceAsync(
            new VariantPriceUpdate(variant, 3999.555m, Actor));

        Assert.Equal("3999.56", await StoredPriceAsync(variant));
        Assert.Equal("""{"selling_price": 3999.56}""", Assert.Single(await AuditsAsync()).NewJson);
    }

    [Fact]
    public async Task A_quantity_change_and_its_audit_row_commit_together()
    {
        var variant = await AddVariantAsync(await AddModelAsync("P2419H"), "SKU-P2419H", quantity: 3);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Commercials(scope.ServiceProvider).UpdateQuantityAsync(
            new VariantQuantityUpdate(variant, 7, Actor));

        Assert.Equal(CommercialUpdateOutcome.Updated, outcome);
        Assert.Equal("7", await StoredQuantityAsync(variant));

        var audit = Assert.Single(await AuditsAsync());

        Assert.Equal("UpdateQuantity", audit.Action);
        Assert.Equal("""{"quantity": 3}""", audit.OldJson);
        Assert.Equal("""{"quantity": 7}""", audit.NewJson);
        Assert.Equal(Actor, audit.UserId);
    }

    [Fact]
    public async Task An_active_state_change_and_its_audit_row_commit_together()
    {
        var variant = await AddVariantAsync(await AddModelAsync("P2419H"), "SKU-P2419H");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Commercials(scope.ServiceProvider).UpdateActiveStateAsync(
            new VariantActiveStateUpdate(variant, IsActive: false, ActorUserId: Actor));

        Assert.Equal(CommercialUpdateOutcome.Updated, outcome);
        Assert.Equal("false", await StoredActiveStateAsync(variant));

        var audit = Assert.Single(await AuditsAsync());

        Assert.Equal("UpdateActiveState", audit.Action);
        Assert.Equal("""{"is_active": true}""", audit.OldJson);
        Assert.Equal("""{"is_active": false}""", audit.NewJson);
        Assert.Equal(Actor, audit.UserId);
    }

    [Fact]
    public async Task A_change_that_changes_nothing_writes_no_audit_row()
    {
        var variant = await AddVariantAsync(
            await AddModelAsync("P2419H"),
            "SKU-P2419H",
            price: 4200m,
            quantity: 3,
            isActive: true);

        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var commercials = Commercials(scope.ServiceProvider);

        Assert.Equal(
            CommercialUpdateOutcome.Unchanged,
            await commercials.UpdatePriceAsync(new VariantPriceUpdate(variant, 4200m, Actor)));
        Assert.Equal(
            CommercialUpdateOutcome.Unchanged,
            await commercials.UpdateQuantityAsync(new VariantQuantityUpdate(variant, 3, Actor)));
        Assert.Equal(
            CommercialUpdateOutcome.Unchanged,
            await commercials.UpdateActiveStateAsync(new VariantActiveStateUpdate(variant, IsActive: true, Actor)));

        Assert.Equal("4200.00", await StoredPriceAsync(variant));
        Assert.Equal("3", await StoredQuantityAsync(variant));
        Assert.Equal("true", await StoredActiveStateAsync(variant));
        Assert.Equal("0", await AuditCountAsync());
    }

    [Fact]
    public async Task An_unknown_variant_changes_nothing_and_is_not_audited()
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var commercials = Commercials(scope.ServiceProvider);

        Assert.Equal(
            CommercialUpdateOutcome.VariantNotFound,
            await commercials.UpdatePriceAsync(new VariantPriceUpdate(4242, 1000m, Actor)));
        Assert.Equal(
            CommercialUpdateOutcome.VariantNotFound,
            await commercials.UpdateQuantityAsync(new VariantQuantityUpdate(4242, 4, Actor)));
        Assert.Equal(
            CommercialUpdateOutcome.VariantNotFound,
            await commercials.UpdateActiveStateAsync(
                new VariantActiveStateUpdate(4242, IsActive: false, Actor)));

        Assert.Equal("0", await AuditCountAsync());
    }

    [Fact]
    public async Task A_rejected_audit_insert_rolls_the_business_change_back()
    {
        var variant = await AddVariantAsync(await AddModelAsync("P2419H"), "SKU-P2419H", price: 4200m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        await RejectAuditInsertsAsync();

        await Assert.ThrowsAsync<DbUpdateException>(
            () => Commercials(scope.ServiceProvider).UpdatePriceAsync(new VariantPriceUpdate(variant, 3500m, Actor)));

        // The change and its audit row are one transaction, so neither survived the failure.
        Assert.Equal("4200.00", await StoredPriceAsync(variant));
        Assert.Equal("0", await AuditCountAsync());
    }

    [Fact]
    public async Task The_stored_quantity_constraint_still_rejects_a_negative_value_directly()
    {
        var variant = await AddVariantAsync(await AddModelAsync("P2419H"), "SKU-P2419H", quantity: 1);

        await Assert.ThrowsAsync<PostgresException>(() => Catalog.ExecuteAsync(
            $"UPDATE catalog.product_variant SET quantity = -1 WHERE id = {variant}"));

        Assert.Equal("1", await StoredQuantityAsync(variant));
    }

    [Fact]
    public async Task An_update_moves_the_stored_updated_at_of_the_variant()
    {
        var variant = await AddVariantAsync(await AddModelAsync("P2419H"), "SKU-P2419H", price: 4200m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        await Commercials(scope.ServiceProvider).UpdatePriceAsync(new VariantPriceUpdate(variant, 4100m, Actor));

        var moved = await Catalog.ScalarAsync(
            $"SELECT (updated_at > created_at)::text FROM catalog.product_variant WHERE id = {variant}");

        Assert.Equal("true", moved);
    }
}
