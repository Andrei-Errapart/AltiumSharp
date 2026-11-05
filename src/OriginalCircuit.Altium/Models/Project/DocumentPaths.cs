using System.Text;

namespace OriginalCircuit.Altium.Models.Project;

// Document paths as Altium, a Windows program, resolves them, on every platform.
internal static class DocumentPaths
{
    private static bool IsSeparator(char c) => c is '\\' or '/';

    // The root of a Windows path: "\\server\" (its share is the first segment), "C:\", "C:" (relative
    // to that drive's current folder), "\" or nothing.
    private static int RootLength(string path)
    {
        if (path.Length >= 2 && IsSeparator(path[0]) && IsSeparator(path[1]))
        {
            var i = 2;
            while (i < path.Length && !IsSeparator(path[i]))
                i++;
            return i < path.Length ? i + 1 : i;
        }
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
            return path.Length >= 3 && IsSeparator(path[2]) ? 3 : 2;
        return path.Length >= 1 && IsSeparator(path[0]) ? 1 : 0;
    }

    // As Path.GetFullPath on Windows resolves the path, without making it absolute.
    public static string Normalize(string path)
    {
        var rootLength = RootLength(path);
        var root = path[..rootLength].Replace('/', '\\');
        var unc = root.StartsWith(@"\\", StringComparison.Ordinal);
        // ".." never climbs above a root; a relative path keeps the ".." it starts with.
        var fullyRooted = unc || root.EndsWith('\\');
        var rest = path.AsSpan(rootLength);
        var endsInSeparator = rest.Length > 0 && IsSeparator(rest[^1]);
        var segments = new List<string>();
        foreach (var range in rest.SplitAny('\\', '/'))
        {
            var segment = rest[range];
            if (segment.IsEmpty || segment is ".")
                continue;
            if (segment is "..")
            {
                if (segments.Count > (unc ? 1 : 0) && segments[^1] != "..")
                    segments.RemoveAt(segments.Count - 1);
                else if (!fullyRooted)
                    segments.Add("..");
                continue;
            }
            // A segment that ends in one period loses it (a share's name is kept as written).
            if (!(unc && segments.Count == 0) && segment[^1] == '.' && (segment.Length == 1 || segment[^2] != '.'))
                segment = segment[..^1];
            segments.Add(segment.ToString());
        }

        var sb = new StringBuilder(root, path.Length + 1);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var last = i == segments.Count - 1;
            // The last segment of a path that does not end in a separator loses its trailing
            // periods and spaces; when nothing is left the path ends at the folder before it.
            if (last && !endsInSeparator && segment != "..")
                segment = segment.TrimEnd(TrailingTrimmed);
            if (last && segment.Length == 0)
                break;
            sb.Append(segment);
            if (!last || endsInSeparator)
                sb.Append('\\');
        }
        return sb.ToString();
    }

    private static readonly char[] TrailingTrimmed = { '.', ' ' };

    // The last segment of the normalized path (after a drive's ':' too), without leading white space
    // or trailing white space and periods; null when nothing is left.
    public static string? FileName(string path)
    {
        var normalized = Normalize(path);
        var name = normalized.AsSpan(normalized.AsSpan().LastIndexOfAny('\\', ':') + 1).TrimStart();
        var end = name.Length;
        while (end > 0 && (name[end - 1] == '.' || char.IsWhiteSpace(name[end - 1])))
            end--;
        return end == 0 ? null : name[..end].ToString();
    }
}
