using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Integration.Tests.Storefront;

/// <summary>
/// Issue #7 acceptance: an approved existing row is updated and immediately visible, a no-op writes
/// nothing, an unapproved key is rejected before persistence, and no update creates a row. Every
/// result a caller can observe is asserted through the Storefront contracts; the direct SQL left here
/// only seeds rows, proves that no row appeared, and holds the row lock for a waiting writer.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BusinessInfoUpdateTests(PostgresContainerFixture postgres) : StorefrontFixture(postgres)
{
    /// <summary>The session name the Storefront host reports, so a test can watch it wait on a lock.</summary>
    private const string WaitingApplication = "storefront-lock-wait-test";

    /// <summary>Bounds a lock-wait test, so a lock that never releases fails instead of hanging CI.</summary>
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task An_approved_existing_row_is_updated_and_the_new_values_are_immediately_visible()
    {
        await SeedAsync(BusinessInfoKeys.Delivery, "قديم");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var before = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Delivery);
        Assert.NotNull(before);

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(
                BusinessInfoKeys.Delivery,
                "  توصيل لكل المحافظات  ",
                "Nationwide delivery",
                IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, outcome);

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Delivery);

        Assert.NotNull(value);
        Assert.Equal("توصيل لكل المحافظات", value.AnswerAr);
        Assert.Equal("Nationwide delivery", value.AnswerEn);
        Assert.True(value.IsActive);
        Assert.True(value.UpdatedAt > before.UpdatedAt);
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

        var before = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Address);
        Assert.NotNull(before);
        Assert.Equal("1 Example Street", before.AnswerEn);

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Address, "شارع 1", answerEn, IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, outcome);

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

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var before = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Warranty);
        Assert.NotNull(before);

        await Assert.ThrowsAsync<ArgumentException>(() => Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Warranty, answerAr!, "30 days", IsActive: true)));

        // The rejected update left the row exactly as it was, its timestamp included.
        Assert.Equal(before, await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Warranty));
    }

    [Fact]
    public async Task An_update_with_the_stored_values_is_unchanged_and_leaves_the_timestamp_alone()
    {
        await SeedAsync(BusinessInfoKeys.Warranty, "ضمان 30 يوما", "30 days");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var before = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Warranty);
        Assert.NotNull(before);

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Warranty, "ضمان 30 يوما", "30 days", IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Unchanged, outcome);

        // A no-op writes nothing at all, so the visible value is the old one down to its timestamp.
        Assert.Equal(before, await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Warranty));
    }

    [Fact]
    public async Task An_update_that_only_differs_by_outer_whitespace_is_unchanged()
    {
        await SeedAsync(BusinessInfoKeys.Warranty, "ضمان 30 يوما");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var before = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Warranty);
        Assert.NotNull(before);

        var outcome = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.Warranty, "  ضمان 30 يوما  ", "  ", IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Unchanged, outcome);

        // The stored row still has no English answer, because nothing was written.
        Assert.Equal(before, await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Warranty));
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
        Assert.Null(await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ReturnExchangePolicy));

        // A row created by mistake would read either as a value or, behind the visibility rule, exactly
        // like a missing one, so the stored rows themselves are the only proof that nothing was written.
        Assert.Equal(0, await RowCountAsync());
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

        // A rejected key never reaches persistence, so the module wrote nothing at all.
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

        // A hidden answer is not a customer-facing fact, so it reads exactly like a missing one.
        Assert.Null(await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ContactPhone));

        var reactivated = await Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.ContactPhone, "0100 000 0000", "0100 000 0000", IsActive: true));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, reactivated);

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.ContactPhone);

        Assert.NotNull(value);
        Assert.True(value.IsActive);
        Assert.Equal("0100 000 0000", value.AnswerAr);
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

        var value = await BusinessInfo(secondScope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.Address);

        Assert.NotNull(value);

        (string AnswerAr, string? AnswerEn)[] written =
        [
            ("شارع 2", "2 Example Street"),
            ("شارع 3", "3 Example Street"),
        ];

        Assert.Contains((value.AnswerAr, value.AnswerEn), written);
    }

    [Fact]
    public async Task An_update_that_waits_for_the_row_lock_records_the_time_it_actually_wrote()
    {
        await SeedAsync(BusinessInfoKeys.WorkingHours, "من 10 ص إلى 8 م");

        await using var host = StartHost(WaitingApplication);
        await using var scope = host.CreateScope();

        // A raw transaction holds the row, so the real update below has to wait for its lock.
        await using var blocker = new NpgsqlConnection(ConnectionString);
        await blocker.OpenAsync();

        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await LockRowAsync(blocker, blockerTransaction, BusinessInfoKeys.WorkingHours);

        using var timeout = new CancellationTokenSource(LockWaitTimeout);

        var update = Updates(scope.ServiceProvider).UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeys.WorkingHours, "من 9 ص إلى 9 م", "09:00 - 21:00", IsActive: true),
            timeout.Token);

        // The update has opened its transaction and is provably waiting on the row lock, so the clock
        // read below happens after that transaction began.
        await WaitUntilSessionWaitsForALockAsync(WaitingApplication, LockWaitTimeout);
        var releasedAt = await PostgresClockTimestampAsync();

        await blockerTransaction.CommitAsync();

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, await update);

        var value = await BusinessInfo(scope.ServiceProvider).GetByKeyAsync(BusinessInfoKeys.WorkingHours);

        Assert.NotNull(value);
        Assert.Equal("من 9 ص إلى 9 م", value.AnswerAr);
        Assert.Equal("09:00 - 21:00", value.AnswerEn);

        // now() would have reported the transaction start, which precedes the lock release, so the
        // stored timestamp has to be the write-time clock instead.
        Assert.True(
            value.UpdatedAt >= releasedAt,
            $"The update recorded {value.UpdatedAt:O} while the row lock was released at {releasedAt:O}.");
    }

    /// <summary>Holds the row of one stored key until the transaction ends.</summary>
    private static async Task LockRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key)
    {
        await using var command = new NpgsqlCommand(
            """SELECT id FROM storefront.business_info WHERE "key" = @key FOR UPDATE;""",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", key);

        await command.ExecuteScalarAsync();
    }
}
