using System.Globalization;
using System.Text;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// Maps an original business-info question to one approved Storefront key through an explicit
/// deterministic allowlist, as the approved Issue #11 decision requires. The customer's text is only
/// matched, never stored and never answered from: the value always comes from Storefront. A question
/// outside the allowlist resolves to nothing, so the turn becomes a clarification instead of a guess.
/// </summary>
public static class BusinessInfoScope
{
    // Keywords are normalized the same way the incoming text is, so an Arabic spelling difference that
    // is purely orthographic (hamza or ya' form, diacritics, tatweel) still resolves. The allowlist
    // itself stays explicit: nothing outside these keywords can resolve to a key.
    private static readonly (string Key, string[] Keywords)[] Entries =
    [
        (BusinessInfoKeyNames.WorkingHours, ["مواعيد", "معاد", "بتفتحوا", "بتقفلوا", "working hours", "opening hours", "hours", "open"]),
        (BusinessInfoKeyNames.Address, ["عنوان", "مكان", "فين", "address", "location", "located"]),
        (BusinessInfoKeyNames.Delivery, ["توصيل", "شحن", "delivery", "deliver", "shipping"]),
        (BusinessInfoKeyNames.PaymentMethods, ["دفع", "فيزا", "كاش", "payment", "visa", "cash", "instapay"]),
        (BusinessInfoKeyNames.Warranty, ["ضمان", "warranty", "guarantee"]),
        (BusinessInfoKeyNames.ContactPhone, ["تليفون", "موبايل", "واتساب", "phone", "contact", "whatsapp"]),
        (BusinessInfoKeyNames.ReturnExchangePolicy, ["استرجاع", "استبدال", "return", "refund", "exchange"]),
    ];

    /// <summary>
    /// The canonical key of the approved concept the question is about, or null when the question does
    /// not name one. When a question names more than one concept, the first entry of the canonical
    /// allowlist wins, so the result is deterministic.
    /// </summary>
    public static string? ResolveKey(string? text)
    {
        var normalized = Normalize(text);

        if (normalized is null)
        {
            return null;
        }

        foreach (var (key, keywords) in Entries)
        {
            foreach (var keyword in keywords)
            {
                if (Contains(normalized, keyword))
                {
                    return key;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The comparison form of a question: lowercased, with Arabic orthographic variants folded and
    /// whitespace collapsed. It is a comparison rule only; it cannot widen the keyword allowlist.
    /// </summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (IsIgnorableMark(character))
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(Fold(character));
        }

        return builder.ToString().ToLower(CultureInfo.InvariantCulture);
    }

    private static bool IsIgnorableMark(char character) =>
        character is '\u0640'
        || (character >= '\u064B' && character <= '\u065F')
        || character == '\u0670'
        || (character >= '\u06D6' && character <= '\u06ED');

    private static char Fold(char character) => character switch
    {
        '\u0622' or '\u0623' or '\u0625' or '\u0671' => '\u0627',
        '\u0649' => '\u064A',
        '\u0629' => '\u0647',
        _ => character,
    };

    /// <summary>
    /// Latin keywords have to sit on word boundaries, so "open" cannot match inside "openssl".
    /// Arabic keywords match as substrings, because Arabic attaches prefixes and suffixes directly.
    /// </summary>
    private static bool Contains(string haystack, string keyword)
    {
        var normalizedKeyword = Normalize(keyword) ?? string.Empty;

        if (normalizedKeyword.Length == 0)
        {
            return false;
        }

        var searchFrom = 0;

        while (true)
        {
            var found = haystack.IndexOf(normalizedKeyword, searchFrom, StringComparison.Ordinal);

            if (found < 0)
            {
                return false;
            }

            if (!IsLatin(normalizedKeyword) || HasBoundaries(haystack, found, normalizedKeyword.Length))
            {
                return true;
            }

            searchFrom = found + 1;
        }
    }

    private static bool IsLatin(string value) => value.All(character => character < '\u0080');

    private static bool HasBoundaries(string haystack, int start, int length)
    {
        var before = start == 0 || !IsWordCharacter(haystack[start - 1]);
        var afterIndex = start + length;
        var after = afterIndex >= haystack.Length || !IsWordCharacter(haystack[afterIndex]);

        return before && after;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character);
}
