using System.Text;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

/// <summary>
/// The bounds that keep a validation diagnostic safe to hand back to the model as the single corrective
/// instruction of the documented retry policy. A reply is untrusted text: it names its own keys, so an
/// undocumented key could be arbitrarily long, carry control characters, or read like an instruction.
/// Every problem that reaches the corrective message is therefore flattened to one line, capped in
/// length, capped in number, and the message as a whole has a fixed maximum size.
/// </summary>
public static class NluDiagnostics
{
    /// <summary>The longest one diagnostic may be, in characters.</summary>
    public const int MaxProblemLength = 200;

    /// <summary>The longest model-authored identifier a diagnostic may quote, in characters.</summary>
    public const int MaxIdentifierLength = 48;

    /// <summary>The most problems the corrective message lists before it summarizes the rest.</summary>
    public const int MaxCorrectionProblems = 8;

    /// <summary>
    /// The most problems any validation result retains. A reply names its own keys, so an undocumented
    /// reply could otherwise allocate one retained diagnostic per hostile key.
    /// </summary>
    public const int MaxRetainedProblems = 16;

    /// <summary>The longest corrective message, in characters, including the fixed omission summary.</summary>
    public const int MaxCorrectionLength = 2048;

    /// <summary>The application-owned summary appended when diagnostics were left out.</summary>
    public const string OmittedProblemsSummary = "Additional validation problems were omitted.";

    /// <summary>
    /// Renders the safe form of a model-authored identifier: letters, digits and <c>_ - .</c> survive,
    /// every other character becomes <c>?</c>, and anything past the cap is elided. An undocumented key
    /// can therefore never add a line break or an unbounded string to a diagnostic.
    /// </summary>
    public static string SanitizeIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var length = Math.Min(identifier.Length, MaxIdentifierLength);
        var builder = new StringBuilder(length + 3);

        for (var index = 0; index < length; index++)
        {
            var character = identifier[index];

            builder.Append(IsSafeIdentifierCharacter(character) ? character : '?');
        }

        if (identifier.Length > MaxIdentifierLength)
        {
            builder.Append("...");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns one diagnostic within <see cref="MaxProblemLength"/>, with control characters flattened to
    /// spaces so a problem always reads as a single line.
    /// </summary>
    public static string ClampProblem(string problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var builder = new StringBuilder(problem.Length);

        foreach (var character in problem)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        var flattened = builder.ToString().Trim();

        if (flattened.Length <= MaxProblemLength)
        {
            return flattened;
        }

        var end = MaxProblemLength - 3;

        if (char.IsHighSurrogate(flattened[end - 1]))
        {
            end--;
        }

        return string.Concat(flattened.AsSpan(0, end), "...");
    }

    /// <summary>
    /// Bounds a problem list: at most <see cref="MaxRetainedProblems"/> clamped diagnostics plus one
    /// fixed application-owned summary when anything was left out.
    /// </summary>
    public static IReadOnlyList<string> ClampProblems(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (problems.Count <= MaxRetainedProblems)
        {
            return [.. problems.Select(ClampProblem)];
        }

        var clamped = new List<string>(MaxRetainedProblems + 1);

        for (var index = 0; index < MaxRetainedProblems; index++)
        {
            clamped.Add(ClampProblem(problems[index]));
        }

        clamped.Add(OmittedProblemsSummary);

        return clamped;
    }

    private static bool IsSafeIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '.';
}
