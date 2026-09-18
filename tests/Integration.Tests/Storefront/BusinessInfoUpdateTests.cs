using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Integration.Tests.Storefront;

/// <summary>
/// Issue #7 acceptance: an approved existing row is updated and immediately visible, a no-op writes
/// nothing, an unapproved key is rejected before persistence, and no update creates a row.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BusinessInfoUpdateTests(PostgresContainerFixture postgres) : StorefrontFixture(postgres)
{
    [Fact]
    public async Task An_approved_existing_row_is_updated_and_the_new_values_are_immediately_visible()
    {
        await SeedAsync(BusinessInfoKeys.Delivery, "قديم");
        var before = await StoredUpdatedAtAsync(BusinessInfoKeys.Delivery);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(
                BusinessInfoKeys.Delivery,
                "  توصيل لكل المحافظات  ",
                "Nationwide delivery",
                IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, outcome);
        Assert.Equal("توصيل لكل المحافظات", await StoredAnswerArAsync(BusinessInfoKeys.Delivery));
        Assert.Equal("Nationwide delivery", await StoredAnswerEnAsync(BusinessInfoKeys.Delivery));
        Assert.NotEqual(before, await StoredUpdatedAtAsync(BusinessInfoKeys.Delivery));

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Delivery);

        Assert.NotNull(value);
        Assert.Equal("توصيل لكل المحافظات", value.AnswerAr);
        Assert.Equal("Nationwide delivery", value.AnswerEn);
        Assert.True(value.IsActive);
        Assert.True(await StoredUpdatedAtIsAsync(BusinessInfoKeys.Delivery, value.UpdatedAt));
        Assert.Equal(1, await RowCountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_english_answer_can_be_cleared_with_null_or_blank(string? answerEn)
    {
        await SeedAsync(BusinessInfoKeys.Address, "شارع 1", "1 Example Street");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Address, "شارع 1", answerEn, IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, outcome);
        Assert.Null(await StoredAnswerEnAsync(BusinessInfoKeys.Address));

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Address);

        Assert.NotNull(value);
        Assert.Null(value.AnswerEn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_arabic_answer_is_rejected_without_writing(string? answerAr)
    {
        await SeedAsync(BusinessInfoKeys.Warranty, "ضمان 30 يوما");
        var before = await StoredUpdatedAtAsync(BusinessInfoKeys.Warranty);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        await Assert.ThrowsAsync<ArgumentException>(() => Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Warranty, answerAr!, "30 days", IsActive: true)));

        Assert.Equal("ضمان 30 يوما", await StoredAnswerArAsync(BusinessInfoKeys.Warranty));
        Assert.Null(await StoredAnswerEnAsync(BusinessInfoKeys.Warranty));
        Assert.Equal(before, await StoredUpdatedAtAsync(BusinessInfoKeys.Warranty));
    }

    [Fact]
    public async Task An_update_with_the_stored_values_is_unchanged_and_leaves_the_timestamp_alone()
    {
        await SeedAsync(BusinessInfoKeys.Warranty, "ضمان 30 يوما", "30 days");
        var before = await StoredUpdatedAtAsync(BusinessInfoKeys.Warranty);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Warranty, "ضمان 30 يوما", "30 days", IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Unchanged, outcome);
        Assert.Equal(before, await StoredUpdatedAtAsync(BusinessInfoKeys.Warranty));
        Assert.Equal("ضمان 30 يوما", await StoredAnswerArAsync(BusinessInfoKeys.Warranty));
    }

    [Fact]
    public async Task An_update_that_only_differs_by_outer_whitespace_is_unchanged()
    {
        await SeedAsync(BusinessInfoKeys.Warranty, "ضمان 30 يوما");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Warranty, "  ضمان 30 يوما  ", "  ", IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Unchanged, outcome);
        Assert.Null(await StoredAnswerEnAsync(BusinessInfoKeys.Warranty));
    }

    [Fact]
    public async Task An_approved_key_without_a_stored_row_is_not_found_and_no_row_is_created()
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(
                BusinessInfoKeys.ReturnExchangePolicy,
                "استبدال خلال 14 يوما",
                "Exchange within 14 days",
                IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.NotFound, outcome);
        Assert.Equal(0, await RowCountAsync());
        Assert.Null(await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ReturnExchangePolicy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("opening-times")]
    [InlineData("working_hours")]
    [InlineData("working hours")]
    [InlineData("FAQ")]
    public async Task A_key_outside_the_allowlist_is_rejected_without_writing(string? key)
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();

        await Assert.ThrowsAsync<ArgumentException>(() => Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(key!, "مواعيد العمل", null, IsActive: true)));

        Assert.Equal(0, await RowCountAsync());
    }

    [Fact]
    public async Task An_active_row_can_be_hidden_and_reactivated()
    {
        await SeedAsync(BusinessInfoKeys.ContactPhone, "0100 000 0000", "0100 000 0000");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var hidden = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.ContactPhone, "0100 000 0000", "0100 000 0000", IsActive: false));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, hidden);
        Assert.Equal("false", await StoredActiveStateAsync(BusinessInfoKeys.ContactPhone));
        Assert.Null(await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ContactPhone));

        var reactivated = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.ContactPhone, "0100 000 0000", "0100 000 0000", IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, reactivated);
        Assert.Equal("true", await StoredActiveStateAsync(BusinessInfoKeys.ContactPhone));

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ContactPhone);

        Assert.NotNull(value);
        Assert.True(value.IsActive);
    }

    [Fact]
    public async Task Concurrent_updates_of_one_key_serialize_and_leave_one_consistent_row()
    {
        await SeedAsync(BusinessInfoKeys.Address, "شارع 1", "1 Example Street");

        await using var host = StartHost();
        await using var firstScope = host.CreateScope();
        await using var secondScope = host.CreateScope();

        var outcomes = await Task.WhenAll(
            Updates(firstScope.ServiceProvider).UpdateAsync(
                new BusinessInfoUpdate(BusinessInfoKeys.Address, "شارع 2", "2 Example Street", IsActive: true)),
            Updates(secondScope.ServiceProvider).UpdateAsync(
                new BusinessInfoUpdate(BusinessInfoKeys.Address, "شارع 3", "3 Example Street", IsActive: true)));

        // The row lock serializes the two writers, and each one commits a complete pair of values.
        Assert.All(outcomes, outcome => Assert.Equal(BusinessInfoUpdateOutcome.Updated, outcome));
        Assert.Equal(1, await RowCountAsync());

        (string? AnswerAr, string? AnswerEn) stored = (
            await StoredAnswerArAsync(BusinessInfoKeys.Address),
            await StoredAnswerEnAsync(BusinessInfoKeys.Address));

        (string? AnswerAr, string? AnswerEn)[] expected =
        [
            ("شارع 2", "2 Example Street"),
            ("شارع 3", "3 Example Street"),
        ];

        Assert.Contains(stored, expected);
    }
}
