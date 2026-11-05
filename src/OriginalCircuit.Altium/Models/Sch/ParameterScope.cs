using OriginalCircuit.Altium.Models.Project;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// The parameters a component's <c>=Name</c> references resolve against after the component's own:
/// a sheet's, then a project's, each indexed once by name (the first of each name, case-insensitive),
/// so resolving every component of a design costs one lookup per reference rather than a scan of
/// every parameter. Build one per sheet and reuse it for that sheet's components (see
/// <see cref="SchComponent.GetResolvedComment(ParameterScope)"/>). It holds the parameters as they
/// were when it was built.
/// </summary>
public sealed class ParameterScope
{
    private readonly Dictionary<string, string?> _sheet;
    private readonly Dictionary<string, string?> _project;

    /// <summary>Indexes the parameters of <paramref name="sheet"/> and <paramref name="project"/> (either may be <c>null</c>).</summary>
    public ParameterScope(SchDocument? sheet, AltiumProject? project)
        : this(SheetValues(sheet), ProjectValues(project))
    {
    }

    private ParameterScope(Dictionary<string, string?> sheet, Dictionary<string, string?> project)
    {
        _sheet = sheet;
        _project = project;
    }

    /// <summary>A scope with no sheet or project parameters.</summary>
    public static ParameterScope Empty { get; } = new((SchDocument?)null, null);

    /// <summary>The same project parameters with another sheet's parameters, reusing the project's index.</summary>
    public ParameterScope WithSheet(SchDocument? sheet) => new(SheetValues(sheet), _project);

    /// <summary>
    /// The value of the sheet's parameter of that name, else the project's, else <c>null</c>. A
    /// parameter that is named but has no value gives <c>null</c>.
    /// </summary>
    public string? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _sheet.TryGetValue(name, out var onSheet) ? onSheet ?? FindInProject(name) : FindInProject(name);
    }

    private string? FindInProject(string name) => _project.GetValueOrDefault(name);

    private static Dictionary<string, string?> SheetValues(SchDocument? sheet)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (sheet is null)
            return values;
        foreach (var parameter in sheet.Parameters)
        {
            if (parameter.Name is { } name)
                values.TryAdd(name, parameter.Value);
        }
        return values;
    }

    // The first [ParameterN] section of each name, in file order, as AltiumProject.Parameters lists them.
    private static Dictionary<string, string?> ProjectValues(AltiumProject? project)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (project is null)
            return values;
        foreach (var parameter in project.Parameters)
        {
            if (parameter.Name is { } name)
                values.TryAdd(name, parameter.Value);
        }
        return values;
    }
}
