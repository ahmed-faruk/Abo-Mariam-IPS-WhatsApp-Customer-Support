using System.Globalization;
using System.Text;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Arabic-Indic (٠١٢٣٤٥٦٧٨٩) and Extended Arabic-Indic (۰۱۲۳۴۵۶۷۸۹) digits. The benchmark
/// dataset validation uses this to prove that a case written with Arabic-Indic digits
/// expects the Western numeric value that the digit string actually denotes.
/// </summary>
public static class ArabicNumerals
{
    private const char ArabicIndicZero = '\u0660';
    private const char ExtendedArabicIndicZero = '\u06F0';

    public static bool ContainsNonAsciiDigits(string text) =>
        text.Any(character => ToWesternDigit(character) is not null && character is not (>= '0' and <= '9'));

    public static string ToWesternDigits(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            builder.Append(ToWesternDigit(character) ?? character);
        }

        return builder.ToString();
    }

    /// <summary>Extracts every integer written in the text, Arabic-Indic digits included.</summary>
    public static IReadOnlyList<long> ExtractIntegers(string text)
    {
        var values = new List<long>();
        var builder = new StringBuilder();

        foreach (var character in text)
        {
            var digit = ToWesternDigit(character) ?? character;

            if (digit is >= '0' and <= '9')
            {
                builder.Append(digit);
                continue;
            }

            Flush(builder, values);
        }

        Flush(builder, values);
        return values;
    }

    private static void Flush(StringBuilder builder, List<long> values)
    {
        if (builder.Length == 0)
        {
            return;
        }

        if (long.TryParse(builder.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            values.Add(value);
        }

        builder.Clear();
    }

    private static char? ToWesternDigit(char character) => character switch
    {
        >= '\u0660' and <= '\u0669' => (char)(character - ArabicIndicZero + '0'),
        >= '\u06F0' and <= '\u06F9' => (char)(character - ExtendedArabicIndicZero + '0'),
        _ => null,
    };
}
