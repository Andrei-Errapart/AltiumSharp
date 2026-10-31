namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Altium parameter references: a parameter value of the form <c>=Name</c> (for example a
/// component Comment of <c>=Value</c> or <c>=Part Number</c>) stands for the value of the
/// parameter called <c>Name</c>.
/// </summary>
public static class ParameterReference
{
    // Bounds how far a chain of references (=A whose value is =B ...) is followed.
    private const int MaxDepth = 16;

    /// <summary>
    /// Whether <paramref name="text"/> is a parameter reference, and if so the referenced
    /// parameter name (trimmed).
    /// </summary>
    public static bool TryGetReferencedName(string? text, out string name)
    {
        name = string.Empty;
        if (text is null || text.Length < 2 || text[0] != '=')
            return false;
        name = text[1..].Trim();
        return name.Length > 0;
    }

    /// <summary>
    /// Resolves <paramref name="text"/> when it is a parameter reference, using
    /// <paramref name="lookup"/> to find a parameter's value by name (<c>null</c> when unknown).
    /// A resolved value that is itself a reference is followed. Text that is not a reference, a
    /// reference whose parameter is unknown, and a circular chain are returned unchanged.
    /// </summary>
    public static string? Resolve(string? text, Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var current = text;
        HashSet<string>? seen = null;
        for (var depth = 0; depth < MaxDepth && TryGetReferencedName(current, out var name); depth++)
        {
            seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!seen.Add(name))
                return text;
            var value = lookup(name);
            if (value is null)
                return text;
            current = value;
        }
        return TryGetReferencedName(current, out _) ? text : current;
    }
}
