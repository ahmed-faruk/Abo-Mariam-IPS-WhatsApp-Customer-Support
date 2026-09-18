using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Integration.Tests.Storefront;

/// <summary>
/// Issue #7 acceptance: a customer-facing read returns the current active stored value of an approved
/// key, and never an inactive value or an invented one.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BusinessInfoReadTests(PostgresContainerFixture postgres) : StorefrontFixture(postgres)
{
    [Fact]
    public async Task The_active_value_of_an_approved_key_is_returned_with_its_stored_timestamp()
    {
        await SeedAsync(BusinessInfoKeys.WorkingHours, "من 10 ص إلى 8 م", "10:00 - 20:00");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.WorkingHours);

        Assert.NotNull(value);
        Assert.Equal(BusinessInfoKeys.WorkingHours, value.Key);
        Assert.Equal("من 10 ص إلى 8 م", value.AnswerAr);
        Assert.Equal("10:00 - 20:00", value.AnswerEn);
        Assert.True(value.IsActive);

        // The timestamp is the stored one, not a value the application computed.
        Assert.True(await StoredUpdatedAtIsAsync(BusinessInfoKeys.WorkingHours, value.UpdatedAt));
    }

    [Fact]
    public async Task A_key_is_read_by_its_canonical_form_whatever_the_case_and_outer_whitespace()
    {
        await SeedAsync(BusinessInfoKeys.PaymentMethods, "كاش أو فيزا");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync("  paymentmethods  ");

        Assert.NotNull(value);
        Assert.Equal(BusinessInfoKeys.PaymentMethods, value.Key);
        Assert.Equal("كاش أو فيزا", value.AnswerAr);
    }

    [Fact]
    public async Task A_row_without_an_english_answer_is_returned_with_a_null_one()
    {
        await SeedAsync(BusinessInfoKeys.ContactPhone, "0100 000 0000");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ContactPhone);

        Assert.NotNull(value);
        Assert.Null(value.AnswerEn);
    }

    [Fact]
    public async Task An_inactive_row_is_not_returned_to_a_customer_facing_caller()
    {
        await SeedAsync(BusinessInfoKeys.Warranty, "ضمان 30 يوما", isActive: false);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Null(await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Warranty));

        // The row is still stored, so an admin can reactivate it later.
        Assert.Equal("false", await StoredActiveStateAsync(BusinessInfoKeys.Warranty));
    }

    [Fact]
    public async Task An_approved_key_without_a_stored_row_has_no_value()
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Null(await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ReturnExchangePolicy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("opening-times")]
    [InlineData("working hours")]
    [InlineData("FAQ")]
    public async Task A_key_outside_the_allowlist_is_rejected_instead_of_being_read(string? key)
    {
        await SeedAsync(BusinessInfoKeys.WorkingHours, "من 10 ص إلى 8 م");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        await Assert.ThrowsAsync<ArgumentException>(
            () => BusinessInfo(scope.ServiceProvider).GetByKeyAsync(key!));
    }
}
