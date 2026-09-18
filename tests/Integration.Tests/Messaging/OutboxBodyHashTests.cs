using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Finding 9: the application hashes the literal UTF-8 bytes of a reply, so the database constraint
/// has to verify exactly those bytes. The old <c>body::bytea</c> cast parsed backslash and "\x"
/// escape notation instead, so valid replies failed the check constraint at enqueue.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxBodyHashTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    /// <summary>
    /// The replies that made the old constraint disagree with the application hash: backslashes,
    /// literal "\x" notation, Arabic text and mixed Arabic/English/symbol text.
    /// </summary>
    public static TheoryData<string> Bodies => new()
    {
        @"C:\photos\new\2026",
        @"\x41",
        @"\\x41\\x42",
        "متاح 24 بوصة IPS بحالة ممتازة",
        @"Dell P2419H 24"" IPS \ 3500 EGP #1",
        @"سعر الشاشة 3500 EGP - Dell P2419H 24"" IPS \ متاح",
        @"\",
        "%_#&*",
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task A_body_is_stored_with_the_hash_of_its_literal_utf8_bytes(string body)
    {
        await using var host = MessagingHost.Start(ConnectionString);
        long id;

        await using (var scope = host.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>()
                .EnqueueAsync(MessagingSamples.Outbound(
                    conversationId: 61,
                    customerExternalId: "20100006001",
                    body: body));
        }

        Assert.True(id > 0);

        // The stored reply is exactly the intent, and its hash is the SHA-256 of the original UTF-8
        // text, both of the application value and of the value the database computes itself.
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        Assert.Equal(body, await Catalog.ScalarAsync(
            $"SELECT body FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal(expectedHash, await Catalog.ScalarAsync(
            $"SELECT encode(body_hash, 'hex') FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal(expectedHash, await Catalog.ScalarAsync(
            "SELECT encode(sha256(convert_to(body, 'UTF8')), 'hex') "
            + $"FROM messaging.outbox_message WHERE id = {id}"));

        // The stored value is still a claimable, unchanged outbound intent.
        Assert.Equal("Pending", await OutboxStatusAsync(id));
    }

    /// <summary>
    /// The regression itself: a hash computed the old way, over the parsed escape bytes of the text,
    /// no longer satisfies the constraint for a reply that literally contains "\x41".
    /// </summary>
    [Fact]
    public async Task The_old_text_bytea_hash_semantics_are_rejected()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => Catalog.ExecuteAsync(
            "INSERT INTO messaging.outbox_message "
            + "(conversation_id, customer_external_id, correlation_id, body, body_hash, partition_key) "
            + @"VALUES (1, '20100006002', 'corr-escape', '\x41', sha256('\x41'::bytea), 'p-escape')"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("ck_outbox_body_hash", exception.Message, StringComparison.Ordinal);

        // The same reply is accepted with the hash of its literal UTF-8 bytes.
        await Catalog.ExecuteAsync(
            "INSERT INTO messaging.outbox_message "
            + "(conversation_id, customer_external_id, correlation_id, body, body_hash, partition_key) "
            + @"VALUES (1, '20100006003', 'corr-escape', '\x41', sha256(convert_to('\x41', 'UTF8')), 'p-escape')");

        Assert.Equal("1", await Catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.outbox_message WHERE body = '\\x41'"));
    }
}
