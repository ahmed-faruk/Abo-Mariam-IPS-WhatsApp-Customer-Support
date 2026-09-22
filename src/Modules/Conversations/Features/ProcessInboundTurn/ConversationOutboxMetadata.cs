using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The tiny Conversations payload stored next to one immutable Outbox reply. It exists for exactly one
/// reason: when a turn stored its reply and then died before its Conversations change was committed, the
/// retry has to reconcile the conversation against the reply the customer really received instead of
/// rendering the reply again from facts that may have changed since.
/// </summary>
/// <remarks>
/// It is reference bookkeeping only: a version, the ordered identifiers of the products the stored reply
/// displayed, and whether that reply is the acknowledgement that hands the conversation to a human. No
/// price, quantity, grade, warranty, specification or business answer is ever written here, because those
/// stay owned by Catalog and Storefront and are read again for every new reply. Messaging stores the
/// serialized form verbatim and never parses it.
/// </remarks>
internal sealed record ConversationOutboxMetadata
{
    /// <summary>The only payload shape this version of the module writes or understands.</summary>
    internal const int CurrentVersion = 1;

    /// <summary>
    /// The most displayed products any stored reply may carry. It is the documented Catalogue search
    /// bound, so a payload can never describe a longer list than a search could have produced, which is
    /// what keeps the stored metadata inside the queue's tiny column.
    /// </summary>
    internal const int MaxDisplayedCandidates = 20;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The metadata of a reply that displays nothing and changes no mode.</summary>
    internal static ConversationOutboxMetadata Empty { get; } = new() { Version = CurrentVersion };

    [JsonPropertyName("v")]
    public int Version { get; init; }

    /// <summary>The ordered products the stored reply displayed, in the order the customer read them.</summary>
    [JsonPropertyName("displayed")]
    public IReadOnlyList<StoredCandidate> DisplayedCandidates { get; init; } = [];

    /// <summary>True when the stored reply is the acknowledgement that hands the conversation to a human.</summary>
    [JsonPropertyName("human")]
    public bool EntersHumanMode { get; init; }

    /// <summary>The stored JSON of this payload, written the same way every time.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Reads the payload of one stored reply. A reply stored before this version carries no payload at
    /// all, which is a null result rather than an error: such a row is still immutable and still owns its
    /// correlation, it simply cannot claim that any product was displayed. A payload that is present but
    /// unreadable, of an unknown version or structurally unsound is an invariant failure and never a
    /// reason to guess, so it throws instead of silently reconciling the wrong thing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The stored payload is not a usable payload of this version.</exception>
    public static ConversationOutboxMetadata? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        ConversationOutboxMetadata? metadata;

        try
        {
            metadata = JsonSerializer.Deserialize<ConversationOutboxMetadata>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The stored Outbox application metadata is not readable, so the accepted reply cannot be "
                + "reconciled.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidOperationException(
                "The stored Outbox application metadata is not readable, so the accepted reply cannot be "
                + "reconciled.",
                exception);
        }

        if (metadata is null
            || metadata.Version != CurrentVersion
            || !IsStructurallyValid(metadata.DisplayedCandidates))
        {
            throw new InvalidOperationException(
                $"The stored Outbox application metadata is not a version {CurrentVersion} payload, so the "
                + "accepted reply cannot be reconciled.");
        }

        return metadata;
    }

    /// <summary>The payload of one reply about to be stored.</summary>
    /// <exception cref="InvalidOperationException">
    /// The displayed candidates are not a valid version 1 payload, which is a programming defect in the
    /// renderer: this shape could never have been displayed, so it must not be stored to be rejected later.
    /// </exception>
    internal static ConversationOutboxMetadata For(
        IEnumerable<ConversationDisplayedCandidate> displayedCandidates,
        bool entersHumanMode)
    {
        ArgumentNullException.ThrowIfNull(displayedCandidates);

        var stored = displayedCandidates
            .Select(candidate => new StoredCandidate
            {
                ModelId = candidate.ModelId,
                VariantId = candidate.VariantId,
            })
            .ToArray();

        if (!IsStructurallyValid(stored))
        {
            throw new InvalidOperationException(
                "A reply may only display a bounded, ordered list of distinct products, so this is not a "
                + "valid version 1 payload and must not be stored.");
        }

        return new ConversationOutboxMetadata
        {
            Version = CurrentVersion,
            DisplayedCandidates = stored,
            EntersHumanMode = entersHumanMode,
        };
    }

    /// <summary>
    /// The one shape a version 1 payload may have: a bounded list of positive identifiers in which a model
    /// appears at most once. The shortlist the conversation builds from this list is one entry per model, so
    /// a repeated model would make two display positions name the same product and is a malformed payload
    /// rather than something to deduplicate. A repeated exact pair is a repeated model, so it is rejected by
    /// the same rule.
    /// </summary>
    private static bool IsStructurallyValid(IReadOnlyList<StoredCandidate>? displayed)
    {
        if (displayed is not { Count: <= MaxDisplayedCandidates })
        {
            return false;
        }

        var models = new HashSet<long>();

        return displayed.All(candidate =>
            candidate is not null
            && candidate.ModelId > 0
            && candidate.VariantId > 0
            && models.Add(candidate.ModelId));
    }

    /// <summary>One displayed product of a stored reply. Names are single letters because the payload is
    /// bounded by the queue and read by nobody but this module.</summary>
    internal sealed record StoredCandidate
    {
        [JsonPropertyName("m")]
        public long ModelId { get; init; }

        [JsonPropertyName("v")]
        public long VariantId { get; init; }
    }
}
