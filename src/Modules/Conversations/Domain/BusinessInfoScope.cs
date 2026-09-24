using System.Globalization;
using System.Text;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// Maps an original business-info question to the approved Storefront keys it names through an explicit
/// deterministic allowlist, as the approved Issue #11 decision requires. The customer's text is only
/// matched, never stored and never answered from: the value always comes from Storefront. A question
/// outside the allowlist resolves to nothing, and a question that names more than one concept resolves
/// to all of them, so the caller asks which answer was meant instead of guessing at one of them.
/// </summary>
public static class BusinessInfoScope
{
    /// <summary>
    /// The spellings Arabic attaches in front of a noun: the definite article, the short prepositions and
    /// the combinations that merge with the article. Only an alias that is itself a noun takes them.
    /// </summary>
    private static readonly string[] NounPrefixes =
    [
        string.Empty, "ال", "و", "ف", "ب", "ك", "ل", "لل", "بال", "وال", "فال", "كال", "بل", "ولل", "فالل", "بالل",
    ];

    /// <summary>An alias that counts only as its own word accepts nothing attached in front of it.</summary>
    private static readonly string[] OwnWordPrefixes = [string.Empty];

    /// <summary>
    /// One approved alias together with the spellings that may stand attached in front of it. The
    /// allowance belongs to the alias rather than to the allowlist as a whole: one set of removable
    /// prefixes for every alias reads unrelated words as concepts, because "ال" + "فين" spells "ألفين"
    /// (two thousand), which is a number and not a place.
    /// </summary>
    private readonly record struct Alias(string Text, string[] AttachedPrefixes)
    {
        /// <summary>A noun alias, which Arabic also writes with the article or a short preposition attached.</summary>
        public static Alias Noun(string text) => new(text, NounPrefixes);

        /// <summary>An alias that is matched only as a whole word of its own.</summary>
        public static Alias OwnWord(string text) => new(text, OwnWordPrefixes);
    }

    // Aliases are normalized the same way the incoming text is, so an Arabic spelling difference that
    // is purely orthographic (hamza or ya' form, diacritics, tatweel) still resolves. The allowlist
    // itself stays explicit: nothing outside these aliases can resolve to a key.
    private static readonly (string Key, Alias[] Aliases)[] Entries =
    [
        (BusinessInfoKeyNames.WorkingHours, [
            Alias.Noun("مواعيد"), Alias.Noun("معاد"), Alias.Noun("بتفتحوا"), Alias.Noun("بتقفلوا"),
            Alias.OwnWord("فاتحين"),
            Alias.OwnWord("working hours"), Alias.OwnWord("opening hours"), Alias.OwnWord("hours"), Alias.OwnWord("open"),
        ]),
        (BusinessInfoKeyNames.Address, [
            Alias.Noun("عنوان"), Alias.Noun("مكان"),
            // "فين" never takes a prefix, because the article in front of it spells the number word
            // "ألفين"; "المكان فين؟" and "فين المكان؟" both still resolve through the standing word.
            Alias.OwnWord("فين"),
            Alias.OwnWord("address"), Alias.OwnWord("location"), Alias.OwnWord("located"),
        ]),
        (BusinessInfoKeyNames.Delivery, [
            Alias.Noun("توصيل"), Alias.Noun("شحن"),
            Alias.OwnWord("delivery"), Alias.OwnWord("deliver"), Alias.OwnWord("shipping"),
        ]),
        (BusinessInfoKeyNames.PaymentMethods, [
            Alias.Noun("دفع"), Alias.Noun("فيزا"), Alias.Noun("كاش"),
            Alias.OwnWord("payment"), Alias.OwnWord("visa"), Alias.OwnWord("cash"), Alias.OwnWord("instapay"),
        ]),
        (BusinessInfoKeyNames.Warranty, [Alias.Noun("ضمان"), Alias.OwnWord("warranty"), Alias.OwnWord("guarantee")]),
        (BusinessInfoKeyNames.ContactPhone, [
            Alias.Noun("تليفون"), Alias.Noun("موبايل"), Alias.Noun("واتساب"),
            Alias.OwnWord("phone"), Alias.OwnWord("contact"), Alias.OwnWord("whatsapp"),
        ]),
        (BusinessInfoKeyNames.ReturnExchangePolicy, [
            Alias.Noun("استرجاع"), Alias.Noun("استبدال"),
            Alias.OwnWord("return"), Alias.OwnWord("refund"), Alias.OwnWord("exchange"),
        ]),
    ];

    /// <summary>
    /// Every distinct approved concept the question names, in the canonical allowlist order. The result
    /// is empty when the question names none, holds exactly one entry when it names exactly one concept,
    /// and holds more than one entry when the question is ambiguous. Repeating an alias of the same
    /// concept still resolves to that one key, so only genuinely different concepts widen the result.
    /// </summary>
    public static IReadOnlyList<string> ResolveKeys(string? text)
    {
        var normalized = Normalize(text);

        if (normalized is null)
        {
            return [];
        }

        var keys = new List<string>();

        foreach (var (key, aliases) in Entries)
        {
            foreach (var alias in aliases)
            {
                if (Contains(normalized, alias))
                {
                    keys.Add(key);

                    break;
                }
            }
        }

        return keys;
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
    /// Latin aliases have to sit on word boundaries, so "open" cannot match inside "openssl". Arabic
    /// aliases match inside their own word, because Arabic writes the definite article, short
    /// prepositions and pronoun suffixes attached to the word: "المكان", "فيزا" and "مواعيدكم" are the
    /// same concepts as "مكان" and "مواعيد". What may stand in front of an alias is decided by that alias
    /// alone, which is what keeps the address alias "مكان" out of "إمكانية" and the address alias "فين"
    /// out of the number word "ألفين".
    /// </summary>
    private static bool Contains(string haystack, Alias alias)
    {
        var normalizedKeyword = Normalize(alias.Text) ?? string.Empty;

        if (normalizedKeyword.Length == 0)
        {
            return false;
        }

        return IsLatin(normalizedKeyword)
            ? ContainsWord(haystack, normalizedKeyword)
            : ContainsArabicWord(haystack, normalizedKeyword, alias.AttachedPrefixes);
    }

    private static bool IsLatin(string value) => value.All(character => character < '\u0080');

    /// <summary>A Latin keyword only counts when the whole word around it is that keyword.</summary>
    private static bool ContainsWord(string haystack, string keyword)
    {
        var searchFrom = 0;

        while (true)
        {
            var found = haystack.IndexOf(keyword, searchFrom, StringComparison.Ordinal);

            if (found < 0)
            {
                return false;
            }

            if (HasBoundaries(haystack, found, keyword.Length))
            {
                return true;
            }

            searchFrom = found + 1;
        }
    }

    private static bool ContainsArabicWord(string haystack, string keyword, string[] allowedPrefixes)
    {
        foreach (var word in haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var searchFrom = 0;

            while (true)
            {
                var found = word.IndexOf(keyword, searchFrom, StringComparison.Ordinal);

                if (found < 0)
                {
                    break;
                }

                if (IsAllowedPrefix(word.AsSpan(0, found), allowedPrefixes))
                {
                    return true;
                }

                searchFrom = found + 1;
            }
        }

        return false;
    }

    private static bool IsAllowedPrefix(ReadOnlySpan<char> prefix, string[] allowedPrefixes)
    {
        foreach (var allowed in allowedPrefixes)
        {
            if (prefix.SequenceEqual(allowed))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBoundaries(string haystack, int start, int length)
    {
        var before = start == 0 || !IsWordCharacter(haystack[start - 1]);
        var afterIndex = start + length;
        var after = afterIndex >= haystack.Length || !IsWordCharacter(haystack[afterIndex]);

        return before && after;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character);
}
